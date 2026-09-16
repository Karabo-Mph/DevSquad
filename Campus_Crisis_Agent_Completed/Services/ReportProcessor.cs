using CampusCrisisAgent.Models;
using Microsoft.Extensions.Logging;

namespace CampusCrisisAgent.Services;

/// <summary>
/// Loads CSVs once, then feeds reports strictly in file order through the agent loop.
/// Implements the complete agent pipeline: Observe → Correlate → Assess → Decide → Act → Monitor → Record
/// Thread-safe for concurrent report processing with cursor-based position tracking.
/// </summary>
public class ReportProcessor
{
    private readonly IncidentStateService _state;
    private readonly CorrelationService _correlation;
    private readonly AssessmentService _assessment;
    private readonly DecisionService _decision;
    private readonly List<CampusReport> _reports = new();
    private readonly object _gate = new();
    private int _cursor;
    private readonly ILogger<ReportProcessor> _logger;

    public ReportProcessor(
        IWebHostEnvironment env,
        IncidentStateService state,
        CorrelationService correlation,
        AssessmentService assessment,
        DecisionService decision,
        ILogger<ReportProcessor> logger)
    {
        _state = state;
        _correlation = correlation;
        _assessment = assessment;
        _decision = decision;
        _logger = logger;

        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        LoadServices(Path.Combine(dataDir, "campus_services.csv"));
        LoadReports(Path.Combine(dataDir, "campus_reports.csv"));

        _logger.LogInformation("ReportProcessor initialized with {ReportCount} reports", _reports.Count);
    }

    public IReadOnlyList<CampusReport> Reports
    {
        get { lock (_gate) return _reports.ToList(); }
    }

    public int RemainingCount
    {
        get { lock (_gate) return Math.Max(0, _reports.Count - _cursor); }
    }

    public int ProcessedCount
    {
        get { lock (_gate) return _cursor; }
    }

    public CampusReport? PeekNext()
    {
        lock (_gate)
        {
            return _cursor < _reports.Count ? _reports[_cursor] : null;
        }
    }

    public CampusReport? LastProcessed { get; private set; }

    public CampusReport? ProcessNext()
    {
        CampusReport report;
        lock (_gate)
        {
            if (_cursor >= _reports.Count)
            {
                _logger.LogInformation("No more reports to process");
                return null;
            }
            report = _reports[_cursor];
            _cursor++;
        }

        _logger.LogInformation("Processing report {ReportId} at position {Position}/{Total}", report.ReportId, _cursor, _reports.Count);
        RunPipeline(report);
        LastProcessed = report;
        return report;
    }

    public int ProcessAll()
    {
        var n = 0;
        while (ProcessNext() != null) n++;
        return n;
    }

    private void RunPipeline(CampusReport report)
    {
        try
        {
            // ===== OBSERVE =====
            // A report is evidence. Normalize missing/malformed fields; never throw.
            Observe(report);
            _logger.LogDebug("Observed report {ReportId}: {Type} at {Location}", report.ReportId, report.Type, report.Location);

            // ===== CORRELATE =====
            // Score against OPEN/MONITORING incidents first. No shared IDs required.
            var correlation = _correlation.Correlate(report, _state);
            _logger.LogDebug("Correlation result for {ReportId}: {Relationship} (score: {Score:0.00})",
                report.ReportId, correlation.Relationship, correlation.BestScore);

            var reopened = false;
            Incident incident;

            if (correlation.Relationship == "update" && correlation.Match != null)
            {
                incident = correlation.Match;
                _logger.LogInformation("Updating existing incident {IncidentId} for report {ReportId}", incident.Id, report.ReportId);
                if (incident.Status == IncidentStatuses.Resolved)
                {
                    // Reopen is applied after assess/decide; flag it here.
                    reopened = true;
                    _logger.LogWarning("Resolved incident {IncidentId} may reopen based on new evidence", incident.Id);
                }
            }
            else
            {
                incident = _state.CreateIncident(report, report.Type, report.Location);
                correlation.Relationship = "new";
                _logger.LogInformation("Created new incident {IncidentId} for report {ReportId}", incident.Id, report.ReportId);
            }

            report.IncidentId = incident.Id;
            report.Relationship = correlation.Relationship;
            _state.AttachReport(incident, report);

            // ===== ASSESS =====
            var assessment = _assessment.Assess(report, incident);
            _state.ApplyAssessment(incident, assessment);
            report.AssignedSeverity = assessment.Severity;
            report.Confidence = assessment.Confidence;
            _logger.LogInformation("Assessment for {ReportId}: {Severity} (confidence: {Confidence:0.00}, conflicting: {Conflicting})",
                report.ReportId, assessment.Severity, assessment.Confidence, assessment.ConflictingEvidence);

            // ===== DECIDE =====
            var decision = _decision.Decide(report, incident, assessment, reopened);
            if (correlation.LooksLikeDuplicate)
                decision.Reason = $"duplicate-like cluster (score {correlation.BestScore:0.00}); {decision.Reason}";
            if (!string.IsNullOrWhiteSpace(correlation.Notes))
                decision.Reason = $"{correlation.Notes}; {decision.Reason}";

            _logger.LogInformation("Decision for {ReportId}: {Decision} → {Service} (status: {Status}, human review: {HumanReview})",
                report.ReportId, decision.Decision, decision.Service, decision.NextStatus, decision.HumanReviewRequired);

            // ===== ACT =====
            // Empty action list is valid — means current response is already sufficient.
            _state.Act(incident, report, decision);

            // ===== MONITOR =====
            _state.ApplyStatus(incident, decision.NextStatus);

            // ===== REASSESS =====
            // New correlated evidence on a resolved incident reopens it.
            if (reopened && decision.NextStatus != IncidentStatuses.Resolved)
            {
                _state.Reopen(incident, decision.Reason);
                _logger.LogWarning("Reopened incident {IncidentId}: {Reason}", incident.Id, decision.Reason);
            }

            // ===== RECORD =====
            // Exactly one predictions.jsonl line per report, appended immediately.
            report.Processed = true;
            _state.Record(report, incident, decision, correlation.Relationship);
            _logger.LogInformation("Successfully processed report {ReportId}", report.ReportId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing report {ReportId}", report.ReportId);
            throw;
        }
    }

    private static void Observe(CampusReport report)
    {
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(report.ReportId))
        {
            report.ReportId = "R-" + Guid.NewGuid().ToString("N")[..8];
            notes.Add("generated report_id");
        }

        if (report.Timestamp == default)
        {
            report.Timestamp = DateTime.UtcNow;
            notes.Add("malformed/missing timestamp — used UtcNow");
        }

        if (string.IsNullOrWhiteSpace(report.Location))
        {
            report.Location = "UNKNOWN";
            notes.Add("missing location — UNKNOWN");
        }

        if (string.IsNullOrWhiteSpace(report.Type))
        {
            report.Type = "UNCLASSIFIED";
            notes.Add("missing type — UNCLASSIFIED");
        }

        report.Description ??= "";
        report.FallbackNotes = string.Join("; ", notes);
    }

    private void LoadReports(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var row in ReadCsv(path))
        {
            var report = new CampusReport
            {
                ReportId = Get(row, "report_id", "id"),
                Location = Get(row, "location"),
                Type = FirstNonEmpty(Get(row, "type"), Get(row, "category")),
                Description = Get(row, "description"),
                ReportedSeverity = Get(row, "reported_severity", "severity"),
                ReporterType = Get(row, "reporter_type")
            };

            var rawTs = Get(row, "timestamp", "time");
            if (!DateTime.TryParse(rawTs, out var ts))
            {
                report.Timestamp = default;
                report.FallbackNotes = string.IsNullOrWhiteSpace(rawTs)
                    ? "missing timestamp"
                    : $"malformed timestamp '{rawTs}'";
            }
            else
            {
                report.Timestamp = ts;
            }

            _reports.Add(report);
        }
    }

    private void LoadServices(string path)
    {
        if (!File.Exists(path)) return;
        var list = new List<CampusService>();
        foreach (var row in ReadCsv(path))
        {
            list.Add(new CampusService
            {
                ServiceId = Get(row, "service_id", "id"),
                Name = Get(row, "name"),
                Type = Get(row, "type"),
                Keywords = Get(row, "keywords")
            });
        }
        _state.LoadServices(list);
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string Get(Dictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return "";
    }

    private static IEnumerable<Dictionary<string, string>> ReadCsv(string path)
    {
        using var reader = new StreamReader(path);
        var headerLine = reader.ReadLine();
        if (headerLine is null) yield break;

        var headers = SplitCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = SplitCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
                row[headers[i]] = i < cells.Count ? cells[i] : "";
            yield return row;
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}

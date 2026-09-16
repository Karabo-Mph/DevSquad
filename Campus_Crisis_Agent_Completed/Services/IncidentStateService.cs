using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CampusCrisisAgent.Models;
using Microsoft.Extensions.Logging;

namespace CampusCrisisAgent.Services;

/// <summary>
/// In-memory store: the only place that mutates incident state.
/// Also appends one predictions.jsonl line per Record() call.
/// </summary>
public class IncidentStateService
{
    private readonly ConcurrentDictionary<string, Incident> _incidents = new();
    private readonly List<DecisionLogEntry> _decisions = new();
    private readonly List<ActionHistoryEntry> _actions = new();
    private readonly List<CampusService> _services = new();
    private readonly object _gate = new();
    private readonly string _predictionsPath;
    private readonly ILogger _logger;
    private int _incidentSeq;

    public IncidentStateService(IWebHostEnvironment env, ILogger<IncidentStateService> logger)
    {
        _logger = logger;
        var outputDir = Path.Combine(env.ContentRootPath, "Output");
        Directory.CreateDirectory(outputDir);
        _predictionsPath = Path.Combine(outputDir, "predictions.jsonl");
        File.WriteAllText(_predictionsPath, string.Empty);
        _logger.LogInformation("IncidentStateService initialized. Predictions will be written to {Path}", _predictionsPath);
    }

    public string PredictionsPath => _predictionsPath;

    public IReadOnlyList<CampusService> Services
    {
        get
        {
            lock (_gate) return _services.ToList();
        }
    }

    public void LoadServices(IEnumerable<CampusService> services)
    {
        try
        {
            lock (_gate)
            {
                _services.Clear();
                _services.AddRange(services);
            }
            _logger.LogInformation("Loaded {ServiceCount} services", _services.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading services");
            throw;
        }
    }

    public IReadOnlyList<Incident> GetIncidents()
    {
        return _incidents.Values
            .OrderByDescending(i => i.LastUpdated)
            .ToList();
    }

    public Incident? GetIncident(string id) =>
        _incidents.TryGetValue(id, out var incident) ? incident : null;

    public IReadOnlyList<Incident> GetOpenOrMonitoring() =>
        _incidents.Values
            .Where(i => i.Status == IncidentStatuses.Open || i.Status == IncidentStatuses.Monitoring)
            .ToList();

    public IReadOnlyList<Incident> GetResolved() =>
        _incidents.Values
            .Where(i => i.Status == IncidentStatuses.Resolved)
            .ToList();

    public IReadOnlyList<DecisionLogEntry> GetDecisionLog()
    {
        lock (_gate)
        {
            return _decisions.OrderByDescending(d => d.Time).ToList();
        }
    }

    public IReadOnlyList<ActionHistoryEntry> GetActionHistory(string? incidentId = null)
    {
        lock (_gate)
        {
            IEnumerable<ActionHistoryEntry> q = _actions;
            if (!string.IsNullOrWhiteSpace(incidentId))
                q = q.Where(a => a.IncidentId == incidentId);
            return q.OrderByDescending(a => a.Time).ToList();
        }
    }

    public Incident CreateIncident(CampusReport report, string type, string location)
    {
        try
        {
            var n = Interlocked.Increment(ref _incidentSeq);
            var id = $"I{n:000}";

            var incident = new Incident
            {
                Id = id,
                Type = string.IsNullOrWhiteSpace(type) ? "UNCLASSIFIED" : type,
                Location = string.IsNullOrWhiteSpace(location) ? "UNKNOWN" : location,
                Status = IncidentStatuses.Open,
                OpenedAt = report.Timestamp == default ? DateTime.UtcNow : report.Timestamp,
                LastUpdated = DateTime.UtcNow
            };

            _incidents[id] = incident;
            _logger.LogInformation("Created incident {IncidentId} for report {ReportId}", id, report.ReportId);
            return incident;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating incident for report {ReportId}", report.ReportId);
            throw;
        }
    }

    public void AttachReport(Incident incident, CampusReport report)
    {
        try
        {
            lock (_gate)
            {
                if (!incident.ReportIds.Contains(report.ReportId))
                    incident.ReportIds.Add(report.ReportId);
                if (!string.IsNullOrWhiteSpace(report.Description))
                    incident.Descriptions.Add(report.Description);
                if (!string.IsNullOrWhiteSpace(report.Location) &&
                    (incident.Location == "UNKNOWN" || incident.Location.Length < report.Location.Length))
                {
                    incident.Location = report.Location;
                }
                if (!string.IsNullOrWhiteSpace(report.Type) && incident.Type == "UNCLASSIFIED")
                    incident.Type = report.Type;
                incident.LastUpdated = DateTime.UtcNow;
            }
            _logger.LogDebug("Attached report {ReportId} to incident {IncidentId}", report.ReportId, incident.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attaching report {ReportId} to incident {IncidentId}", report.ReportId, incident.Id);
            throw;
        }
    }

    public void ApplyAssessment(Incident incident, AssessmentResult assessment)
    {
        try
        {
            lock (_gate)
            {
                incident.Severity = assessment.Severity;
                incident.Confidence = assessment.Confidence;
                incident.HasConflictingEvidence = assessment.ConflictingEvidence;
                incident.Summary = assessment.Rationale;
                incident.LastUpdated = DateTime.UtcNow;
            }
            _logger.LogDebug("Applied assessment to incident {IncidentId}: {Severity}", incident.Id, assessment.Severity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying assessment to incident {IncidentId}", incident.Id);
            throw;
        }
    }

    public void ApplyStatus(Incident incident, string status)
    {
        lock (_gate)
        {
            incident.Status = status;
            incident.LastUpdated = DateTime.UtcNow;
        }
    }

    public void Reopen(Incident incident, string reason)
    {
        lock (_gate)
        {
            incident.Status = IncidentStatuses.Open;
            incident.Summary = reason;
            incident.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>Act: record dispatch/monitor/close actions; skip when the action list is empty.</summary>
    public void Act(Incident incident, CampusReport report, ActionDecision decision)
    {
        lock (_gate)
        {
            if (decision.Actions.Count == 0)
            {
                incident.LastAction = decision.Decision;
                incident.LastUpdated = DateTime.UtcNow;
                return;
            }

            foreach (var action in decision.Actions)
            {
                if (!string.IsNullOrWhiteSpace(action.Service) && !incident.Services.Contains(action.Service))
                    incident.Services.Add(action.Service);

                _actions.Add(new ActionHistoryEntry
                {
                    IncidentId = incident.Id,
                    ReportId = report.ReportId,
                    Action = action.Type,
                    Service = action.Service,
                    Time = DateTime.UtcNow,
                    Outcome = string.IsNullOrWhiteSpace(decision.NextStatus) ? incident.Status : decision.NextStatus
                });
            }

            incident.LastAction = decision.Decision;
            incident.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>Record: one decision-log row and one jsonl line per processed report.</summary>
    public void Record(CampusReport report, Incident incident, ActionDecision decision, string relationship)
    {
        var entry = new DecisionLogEntry
        {
            ReportId = report.ReportId,
            IncidentId = incident.Id,
            Relationship = relationship,
            Severity = incident.Severity,
            Confidence = incident.Confidence,
            Decision = decision.Decision,
            Service = decision.Service,
            Status = incident.Status,
            Reason = decision.Reason,
            Time = DateTime.UtcNow,
            HumanReviewRequired = decision.HumanReviewRequired
        };

        lock (_gate)
        {
            _decisions.Add(entry);
            AppendPredictionLine(entry);
        }
    }

    private void AppendPredictionLine(DecisionLogEntry entry)
    {
        try
        {
            var payload = new
            {
                report_id = entry.ReportId,
                incident_id = entry.IncidentId,
                relationship = entry.Relationship,
                severity = entry.Severity,
                confidence = Math.Round(entry.Confidence, 4),
                decision = entry.Decision,
                service = entry.Service,
                status = entry.Status,
                reason = entry.Reason
            };

            var line = JsonSerializer.Serialize(payload) + Environment.NewLine;
            File.AppendAllText(_predictionsPath, line, Encoding.UTF8);
            _logger.LogDebug("Appended prediction for report {ReportId} to {Path}", entry.ReportId, _predictionsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error appending prediction for report {ReportId} to {Path}", entry.ReportId, _predictionsPath);
            throw;
        }
    }
}

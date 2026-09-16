using CampusCrisisAgent.Models;
using Microsoft.Extensions.Logging;

namespace CampusCrisisAgent.Services;

/// <summary>
/// Rule-based severity + confidence. Escalates from multiple weak signals and can drop
/// confidence when evidence conflicts. Calls ILlmClient only when one is registered in DI.
/// </summary>
public class AssessmentService
{
    private readonly ILlmClient? _llm;
    private readonly ILogger _logger;

    public AssessmentService(IEnumerable<ILlmClient> llmClients, ILogger<AssessmentService> logger)
    {
        _llm = llmClients.FirstOrDefault();
        _logger = logger;
        _logger.LogInformation("AssessmentService initialized with LLM client: {HasLlm}", _llm != null);
    }

    public AssessmentResult Assess(CampusReport report, Incident? incident)
    {
        var ruleBased = AssessRules(report, incident);

        if (_llm is null)
        {
            _logger.LogDebug("Using rule-based assessment for report {ReportId}", report.ReportId);
            return ruleBased;
        }

        try
        {
            var prompt = BuildPrompt(report, incident, ruleBased);
            _logger.LogDebug("Calling LLM for report {ReportId}", report.ReportId);
            var raw = _llm.AssessAsync(prompt).GetAwaiter().GetResult();
            if (TryParseLlm(raw, ruleBased, out var parsed))
            {
                _logger.LogInformation("LLM assessment successful for report {ReportId}", report.ReportId);
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM assessment failed for report {ReportId}, falling back to rule-based", report.ReportId);
           
        }

        return ruleBased;
    }

    private static AssessmentResult AssessRules(CampusReport report, Incident? incident)
    {
        var text = $"{report.Type} {report.Description} {report.ReportedSeverity}".ToLowerInvariant();
        var signal = KeywordSeverity(text);
        var conflict = LooksLikeAllClear(text) && signal.Rank >= 2;

        var rank = signal.Rank;
        var confidence = signal.Confidence;
        var historyCount = incident?.ReportIds.Count ?? 0;

        if (incident != null && historyCount > 0)
        {
            var priorRank = RankOf(incident.Severity);
            if (!conflict && !LooksLikeAllClear(text))
            {
                // Several minor reports can raise severity over time.
                // Formula: NewRank = Min(3, PriorRank + Min(2, HistoryCount/2 + SignalBoost))
                // SignalBoost = 1 if current signal >= prior severity, else 0
                var stacked = Math.Min(3, priorRank + Math.Min(2, historyCount / 2 + (signal.Rank >= priorRank ? 1 : 0)));
                if (signal.Rank >= 1)
                    rank = Math.Max(priorRank, stacked);
                else
                    rank = Math.Max(priorRank, signal.Rank);

                // Confidence increases with historical evidence
                // Formula: NewConfidence = Min(0.95, PriorConfidence + 0.08 + 0.04 × Min(HistoryCount, 4))
                confidence = Math.Min(0.95, incident.Confidence + 0.08 + 0.04 * Math.Min(historyCount, 4));
            }

            // Conflict penalty: reduce severity and confidence when evidence conflicts
            if (conflict || incident.HasConflictingEvidence)
            {
                rank = Math.Max(1, Math.Max(priorRank - 1, signal.Rank - 1));
                confidence = Math.Max(0.25, Math.Min(incident.Confidence, signal.Confidence) - 0.2);
            }
        }

        if (LooksLikeAllClear(text) && !HasHazard(text))
        {
            rank = Math.Min(rank, 1);
            confidence = Math.Max(confidence, 0.7);
            conflict = incident != null && RankOf(incident.Severity) >= 2;
        }

        if (!string.IsNullOrWhiteSpace(report.FallbackNotes))
            confidence = Math.Max(0.2, confidence - 0.1);

        var severity = SeverityOf(rank);
        var rationale = conflict
            ? $"conflicting evidence; severity {severity} at {confidence:0.00}"
            : $"rule signal '{signal.Label}' stacked with {historyCount} prior report(s)";

        return new AssessmentResult
        {
            Severity = severity,
            Confidence = Math.Clamp(confidence, 0.05, 0.99),
            ConflictingEvidence = conflict,
            Rationale = rationale
        };
    }

    /// <summary>
    /// Analyzes text for keywords to determine severity rank and confidence.
    /// Prioritizes critical keywords that indicate immediate danger.
    /// </summary>
    /// <param name="text">The text to analyze (normalized to lowercase).</param>
    /// <returns>A tuple containing severity rank (0-3), confidence (0.0-1.0), and label describing the match.</returns>
    private static (int Rank, double Confidence, string Label) KeywordSeverity(string text)
    {
        // Rank 3 (CRITICAL): Immediate life-threatening situations
        if (ContainsAny(text, "active shooter", "explosion", "gunshot", "not breathing", "unconscious", "collapse"))
            return (3, 0.9, "critical-keyword");
        if (ContainsAny(text, "fire", "flames", "assault", "violence", "gas leak", "weapon", "burning smell", "sparks"))
            return (3, 0.85, "critical-hazard");

        // Rank 2 (HIGH): Serious hazards requiring immediate attention
        if (ContainsAny(text, "smoke", "injury", "injured", "bleeding", "threat", "flood", "electrical spark", "alarm activated"))
            return (2, 0.75, "high-hazard");

        // Rank 1 (MEDIUM): Moderate issues requiring attention
        if (ContainsAny(text, "alarm", "leak", "outage", "unsafe", "medical", "smell", "spark", "loose", "broken", "blocking"))
            return (1, 0.65, "medium-signal");

        // Rank 0 (LOW): Minor issues or maintenance requests
        if (ContainsAny(text, "wifi", "noise", "litter", "lost", "slow", "insects", "overflowing", "flickering"))
            return (0, 0.7, "low-signal");

        // Default: Medium severity for unknown issues
        return (1, 0.5, "default-medium");
    }

    private static bool LooksLikeAllClear(string text) =>
        ContainsAny(text, "false alarm", "all clear", "resolved", "nothing found", "already handled", "ok now", "no fire", "dust", "no remaining", "removed", "restored", "reopened", "completed", "stable");

    private static bool HasHazard(string text) =>
        ContainsAny(text, "fire", "smoke", "injury", "assault", "leak", "weapon", "unconscious");

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(text.Contains);

    private static int RankOf(string severity) => severity.ToUpperInvariant() switch
    {
        "CRITICAL" => 3,
        "HIGH" => 2,
        "MEDIUM" => 1,
        _ => 0
    };

    private static string SeverityOf(int rank) => rank switch
    {
        >= 3 => "CRITICAL",
        2 => "HIGH",
        1 => "MEDIUM",
        _ => "LOW"
    };

    private static string BuildPrompt(CampusReport report, Incident? incident, AssessmentResult fallback)
    {
        var existing = incident is null
            ? "none"
            : $"{incident.Id} {incident.Severity} reports={incident.ReportIds.Count}";

        return
            "You are a campus incident assessor. Return JSON only:\n" +
            "{\"severity\":\"LOW|MEDIUM|HIGH|CRITICAL\",\"confidence\":0.0,\"conflicting\":false,\"reason\":\"...\"}\n" +
            $"Report: {report.Description}\n" +
            $"Location: {report.Location}\n" +
            $"Type: {report.Type}\n" +
            $"Existing incident: {existing}\n" +
            $"Rule-based hint: {fallback.Severity} {fallback.Confidence:0.00}";
    }

    private static bool TryParseLlm(string raw, AssessmentResult fallback, out AssessmentResult parsed)
    {
        parsed = fallback;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            var severity = root.TryGetProperty("severity", out var s) ? s.GetString() ?? fallback.Severity : fallback.Severity;
            var confidence = root.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var d) ? d : fallback.Confidence;
            var conflicting = root.TryGetProperty("conflicting", out var cf) && cf.ValueKind is System.Text.Json.JsonValueKind.True;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? fallback.Rationale : fallback.Rationale;
            parsed = new AssessmentResult
            {
                Severity = severity.ToUpperInvariant(),
                Confidence = Math.Clamp(confidence, 0.05, 0.99),
                ConflictingEvidence = conflicting,
                Rationale = "llm:" + reason
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start) return raw[start..(end + 1)];
        return raw;
    }
}

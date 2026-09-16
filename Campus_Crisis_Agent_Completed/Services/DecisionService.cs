using CampusCrisisAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusCrisisAgent.Services;

/// <summary>
/// Maps (severity, confidence, status) → (action, service, humanReview).
/// Returns an EMPTY action list when no new action is warranted (no duplicate dispatch).
/// Decision logic:
/// - Critical severity always requires human review
/// - Low confidence triggers human review
/// - Conflicting evidence triggers human review
/// - Already dispatched services get empty action list (duplicate prevention)
/// - All-clear reports close incidents
/// </summary>
public class DecisionService
{
    private readonly ILogger _logger;
    private readonly DecisionSettings _settings;

    public DecisionService(ILogger<DecisionService> logger, IOptions<DecisionSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _logger.LogInformation("DecisionService initialized with HumanReviewConfidenceFloor: {Floor}", _settings.HumanReviewConfidenceFloor);
    }

    public ActionDecision Decide(CampusReport report, Incident incident, AssessmentResult assessment, bool reopened)
    {
        var service = PickService(report, incident);
        _logger.LogDebug("Selected service {Service} for report {ReportId}", service, report.ReportId);

        var humanReview =
            assessment.Confidence < _settings.HumanReviewConfidenceFloor ||
            assessment.ConflictingEvidence ||
            assessment.Severity == "CRITICAL" ||
            !string.IsNullOrWhiteSpace(report.FallbackNotes);

        var alreadyDispatched = incident.Services.Contains(service);
        var allClear = LooksResolved(report);
        var reasonParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(report.FallbackNotes))
            reasonParts.Add(report.FallbackNotes);
        reasonParts.Add(assessment.Rationale);

        if (reopened)
        {
            reasonParts.Add("resolved incident reopened on new correlated evidence");
            if (!alreadyDispatched && NeedsDispatch(assessment.Severity))
            {
                return Build("REOPEN_DISPATCH", service, IncidentStatuses.Open, humanReview, reasonParts,
                    new PlannedAction { Type = "DISPATCH", Service = service });
            }

            return Build("REOPEN_MONITOR", alreadyDispatched ? service : "", IncidentStatuses.Open, humanReview, reasonParts);
        }

        if (allClear && assessment.Severity is "LOW" or "MEDIUM")
        {
            reasonParts.Add("evidence indicates all-clear; no new dispatch");
            return Build("CLOSE", service, IncidentStatuses.Resolved, humanReview, reasonParts,
                new PlannedAction { Type = "CLOSE", Service = service });
        }

        if (NeedsDispatch(assessment.Severity))
        {
            if (alreadyDispatched)
            {
                var next = assessment.Severity == "CRITICAL" ? IncidentStatuses.Open : IncidentStatuses.Monitoring;
                reasonParts.Add($"{service} already engaged — empty action list");
                return Build("NO_ACTION", service, next, humanReview, reasonParts);
            }

            var status = assessment.Severity == "LOW" ? IncidentStatuses.Monitoring : IncidentStatuses.Open;
            reasonParts.Add($"dispatch {service} for {assessment.Severity}");
            return Build("DISPATCH", service, status, humanReview, reasonParts,
                new PlannedAction { Type = "DISPATCH", Service = service });
        }

        if (incident.Status == IncidentStatuses.Open && assessment.Severity == "LOW" && assessment.Confidence >= 0.6)
        {
            reasonParts.Add("low severity with stable confidence — monitor only");
            return Build("MONITOR", service, IncidentStatuses.Monitoring, humanReview, reasonParts,
                new PlannedAction { Type = "MONITOR", Service = service });
        }

        reasonParts.Add("current response is sufficient");
        return Build("NO_ACTION", alreadyDispatched ? service : "", incident.Status, humanReview, reasonParts);
    }

    private static bool NeedsDispatch(string severity) =>
        severity is "MEDIUM" or "HIGH" or "CRITICAL";

    private static bool LooksResolved(CampusReport report)
    {
        var text = (report.Description ?? "").ToLowerInvariant();
        return text.Contains("all clear") || text.Contains("false alarm") || text.Contains("already handled")
               || text.Contains("resolved") || text.Contains("nothing found");
    }

    /// <summary>
    /// Selects the appropriate service based on keyword analysis of report type, description, and incident type.
    /// Uses hierarchical keyword matching to find the most relevant service.
    /// Service priority: Critical emergency services first, then specialized services, then general management.
    /// </summary>
    /// <param name="report">The campus report to analyze.</param>
    /// <param name="incident">The existing incident (for context).</param>
    /// <returns>Service ID string (e.g., "SVC-FIRE", "SVC-EMS", "SVC-IT").</returns>
    private static string PickService(CampusReport report, Incident incident)
    {
        var hay = $"{report.Type} {report.Description} {incident.Type}".ToLowerInvariant();

        // Critical emergency services (from official campus_services.csv)
        if (Contains(hay, "fire", "smoke", "flames", "burning", "electrical spark")) return "SVC-FIRE";
        if (Contains(hay, "ambulance", "unconscious", "not breathing", "cardiac", "collapsed")) return "SVC-EMS";
        if (Contains(hay, "injur", "medical", "blood", "ill", "first aid")) return "SVC-MEDICAL";
        if (Contains(hay, "assault", "violence", "weapon", "theft", "suspicious", "security")) return "SVC-SECURITY";

        // Specialized services (from official campus_services.csv)
        if (Contains(hay, "electric", "power", "spark", "wiring")) return "SVC-ELECTRICAL";
        if (Contains(hay, "wifi", "network", "server", "computer", "it ", "authentication")) return "SVC-IT";
        if (Contains(hay, "lock", "access", "badge", "wheelchair", "accessible")) return "SVC-ACCESS";
        if (Contains(hay, "spill", "litter", "cleaning", "environmental", "hygiene")) return "SVC-CLEANING";
        if (Contains(hay, "counsel", "mental", "distress", "wellness")) return "SVC-COUNSELLING";
        if (Contains(hay, "announce", "evac", "communication")) return "SVC-COMMS";
        if (Contains(hay, "leak", "hvac", "lift", "elevator", "building", "plumbing", "glass")) return "SVC-FACILITIES";

        // Default to general management
        return "SVC-MANAGEMENT";
    }

    private static bool Contains(string hay, params string[] needles) =>
        needles.Any(hay.Contains);

    private static ActionDecision Build(
        string decision,
        string service,
        string status,
        bool humanReview,
        IEnumerable<string> reasons,
        PlannedAction? action = null)
    {
        var actions = new List<PlannedAction>();
        if (action != null) actions.Add(action);

        return new ActionDecision
        {
            Decision = decision,
            Service = service,
            NextStatus = status,
            HumanReviewRequired = humanReview,
            Reason = string.Join("; ", reasons.Where(r => !string.IsNullOrWhiteSpace(r))),
            Actions = actions
        };
    }
}

namespace CampusCrisisAgent.Models;

public class DashboardViewModel
{
    public CampusReport? IncomingReport { get; set; }
    public bool IncomingIsQueued { get; set; }
    public int RemainingCount { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalReports { get; set; }
    public IReadOnlyList<DecisionLogEntry> DecisionLog { get; set; } = Array.Empty<DecisionLogEntry>();
    public IReadOnlyList<Incident> Incidents { get; set; } = Array.Empty<Incident>();
    public IReadOnlyList<ActionHistoryEntry> ActionHistory { get; set; } = Array.Empty<ActionHistoryEntry>();
    public string? ActionFilterIncidentId { get; set; }
    public string PredictionsPath { get; set; } = "";
}

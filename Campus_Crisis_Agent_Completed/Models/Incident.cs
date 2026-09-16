namespace CampusCrisisAgent.Models;

public static class IncidentStatuses
{
    public const string Open = "Open";
    public const string Monitoring = "Monitoring";
    public const string Resolved = "Resolved";
}

public class Incident
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Location { get; set; } = "";
    public string Severity { get; set; } = "LOW";
    public double Confidence { get; set; }
    public string Status { get; set; } = IncidentStatuses.Open;
    public List<string> ReportIds { get; } = new();
    public List<string> Services { get; } = new();
    public DateTime LastUpdated { get; set; }
    public DateTime OpenedAt { get; set; }
    public string LastAction { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Descriptions { get; } = new();
    public bool HasConflictingEvidence { get; set; }
}

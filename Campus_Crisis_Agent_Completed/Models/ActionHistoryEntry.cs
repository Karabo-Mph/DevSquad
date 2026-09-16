namespace CampusCrisisAgent.Models;

public class ActionHistoryEntry
{
    public string IncidentId { get; set; } = "";
    public string ReportId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Service { get; set; } = "";
    public DateTime Time { get; set; }
    public string Outcome { get; set; } = "";
}

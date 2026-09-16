namespace CampusCrisisAgent.Models;

public class DecisionLogEntry
{
    public string ReportId { get; set; } = "";
    public string IncidentId { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string Severity { get; set; } = "";
    public double Confidence { get; set; }
    public string Decision { get; set; } = "";
    public string Service { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime Time { get; set; }
    public bool HumanReviewRequired { get; set; }
}

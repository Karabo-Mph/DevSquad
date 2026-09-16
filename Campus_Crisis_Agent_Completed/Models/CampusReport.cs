namespace CampusCrisisAgent.Models;

/// <summary>
/// Placeholder CSV columns: report_id, timestamp, location, type, description.
/// Rename properties / CSV headers together when the real schema arrives.
/// Extra columns (category, reported_severity, reporter_type) are optional and ignored if absent.
/// </summary>
public class CampusReport
{
    public string ReportId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Location { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";

    // Optional columns — safe to leave empty.
    public string ReportedSeverity { get; set; } = "";
    public string ReporterType { get; set; } = "";

    public bool Processed { get; set; }
    public string IncidentId { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string AssignedSeverity { get; set; } = "";
    public double Confidence { get; set; }
    public string FallbackNotes { get; set; } = "";
}

namespace Campus_Crisis_Agent.Models;
public class CampusReport {
 public string ReportId {get;set;}="";
 public DateTime Timestamp {get;set;}
 public string Location {get;set;}="";
 public string Category {get;set;}="";
 public string ReportedSeverity {get;set;}="";
 public string Description {get;set;}="";
 public string ReporterType {get;set;}="";
 public string Priority {get;set;}="";
 public string RecommendedAction {get;set;}="";
}
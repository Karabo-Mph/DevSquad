using Campus_Crisis_Agent.Models;
namespace Campus_Crisis_Agent.Services;
public class ReportAgent {
 public void Triage(CampusReport r) {
  var text=(r.Description+" "+r.Category+" "+r.ReportedSeverity).ToLower();
  r.Priority=text.Contains("emergency")||text.Contains("fire")||text.Contains("violence")||text.Contains("medical")?"Critical":
   text.Contains("urgent")||text.Contains("unsafe")||text.Contains("threat")?"High":"Normal";
  r.RecommendedAction=r.Priority=="Critical"?"Contact campus security/emergency services immediately":
   r.Priority=="High"?"Escalate to campus safety and notify the responsible department":
   "Log report and assign it to the relevant campus department";
 }
}
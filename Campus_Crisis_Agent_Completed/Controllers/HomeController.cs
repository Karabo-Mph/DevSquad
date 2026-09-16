using Microsoft.AspNetCore.Mvc;
using Campus_Crisis_Agent.Models;
using Campus_Crisis_Agent.Services;
namespace Campus_Crisis_Agent.Controllers;
public class HomeController : Controller {
 static readonly List<CampusReport> Reports=new();
 readonly ReportAgent agent;
 public HomeController(ReportAgent agent){this.agent=agent;}
 public IActionResult Index(string? search,string? priority){
  var data=Reports.Where(x=>(string.IsNullOrWhiteSpace(search)||($"{x.Location} {x.Category} {x.Description}").Contains(search,StringComparison.OrdinalIgnoreCase))&&(string.IsNullOrWhiteSpace(priority)||x.Priority==priority)).ToList();
  ViewBag.Total=Reports.Count; ViewBag.Critical=Reports.Count(x=>x.Priority=="Critical"); ViewBag.High=Reports.Count(x=>x.Priority=="High");
  return View(data);
 }
 [HttpPost] public IActionResult Create(CampusReport report){report.ReportId=Guid.NewGuid().ToString("N")[..8];report.Timestamp=DateTime.Now;agent.Triage(report);Reports.Add(report);return RedirectToAction(nameof(Index));}
}
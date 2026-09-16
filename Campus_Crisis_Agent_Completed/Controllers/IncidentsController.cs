using CampusCrisisAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusCrisisAgent.Controllers;

public class IncidentsController : Controller
{
    private readonly IncidentStateService _state;
    private readonly ReportProcessor _processor;

    public IncidentsController(IncidentStateService state, ReportProcessor processor)
    {
        _state = state;
        _processor = processor;
    }

    public IActionResult Index()
    {
        return View(_state.GetIncidents());
    }

    public IActionResult Details(string id)
    {
        var incident = _state.GetIncident(id);
        if (incident is null) return NotFound();

        ViewBag.Reports = _processor.Reports.Where(r => r.IncidentId == id).ToList();
        ViewBag.Actions = _state.GetActionHistory(id);
        ViewBag.Decisions = _state.GetDecisionLog().Where(d => d.IncidentId == id).ToList();
        return View(incident);
    }
}

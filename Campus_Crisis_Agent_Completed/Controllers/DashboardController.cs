using CampusCrisisAgent.Models;
using CampusCrisisAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusCrisisAgent.Controllers;

public class DashboardController : Controller
{
    private readonly ReportProcessor _processor;
    private readonly IncidentStateService _state;

    public DashboardController(ReportProcessor processor, IncidentStateService state)
    {
        _processor = processor;
        _state = state;
    }

    public IActionResult Index(string? incidentId)
    {
        return View(BuildModel(incidentId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ProcessNext()
    {
        _processor.ProcessNext();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ProcessAll()
    {
        _processor.ProcessAll();
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Error()
    {
        return View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }

    private DashboardViewModel BuildModel(string? incidentId)
    {
        var next = _processor.PeekNext();
        var last = _processor.LastProcessed;
        var incoming = next ?? last;
        var queued = next is not null;

        var history = _state.GetActionHistory(incidentId);

        return new DashboardViewModel
        {
            IncomingReport = incoming,
            IncomingIsQueued = queued,
            RemainingCount = _processor.RemainingCount,
            ProcessedCount = _processor.ProcessedCount,
            TotalReports = _processor.Reports.Count,
            DecisionLog = _state.GetDecisionLog(),
            Incidents = _state.GetIncidents(),
            ActionHistory = history,
            ActionFilterIncidentId = incidentId,
            PredictionsPath = _state.PredictionsPath
        };
    }
}

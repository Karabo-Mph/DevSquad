using CampusCrisisAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusCrisisAgent.Controllers;

public class ReportsController : Controller
{
    private readonly ReportProcessor _processor;

    public ReportsController(ReportProcessor processor)
    {
        _processor = processor;
    }

    public IActionResult Index()
    {
        return View(_processor.Reports);
    }
}

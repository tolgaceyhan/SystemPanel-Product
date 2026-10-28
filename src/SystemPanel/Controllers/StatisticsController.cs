using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using SystemPanel.Security;
using SystemPanel.Services;

namespace systempanelmodern.Controllers;

[RequirePermission(Permissions.DashboardView)]
public class StatisticsController : Controller
{
    private readonly IStatisticsService _service;

    public StatisticsController(IStatisticsService service)
    {
        _service = service;
    }

    [HttpGet("/api/statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        var statistics = await _service.GetStatisticsAsync();
        return Ok(statistics);
    }

    public async Task<IActionResult> Details()
    {
        var statistics = await _service.GetStatisticsAsync();
        return View(statistics);
    }
}

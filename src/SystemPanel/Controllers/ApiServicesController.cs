using SystemPanel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SystemPanel.Services;

namespace SystemPanel.Controllers;

[Route("api/services")]

[RequirePermission(Permissions.ServersView)]
public class ApiServicesController : BaseController
{
    private readonly IServiceStatusService _serviceStatusService;

    // Constructor now includes ILogger injection
    public ApiServicesController(IServiceStatusService serviceStatusService, ILogger<ApiServicesController> logger)
        : base(logger) // Pass the logger to the base constructor
    {
        _serviceStatusService = serviceStatusService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllServices()
    {
        var services = await _serviceStatusService.GetAllServiceStatusesAsync();
        return Ok(services);
    }

    [HttpPost("{id}/start")]
    [ValidateAntiForgeryToken]
    [RequirePermission(Permissions.ServersServicesControl)]
    public async Task<IActionResult> StartService(int id)
    {
        await _serviceStatusService.StartServiceAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/stop")]
    [ValidateAntiForgeryToken]
    [RequirePermission(Permissions.ServersServicesControl)]
    public async Task<IActionResult> StopService(int id)
    {
        await _serviceStatusService.StopServiceAsync(id);
        return NoContent();
    }

    [HttpGet("details/{id}")]
    public IActionResult ServiceDetails(int id)
    {
        var service = _serviceStatusService.GetServiceStatusById(id);
        if (service == null)
        {
            return NotFound();
        }

        return Ok(service);  // Returning service details in JSON format
    }
}

using SystemPanel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using SystemPanel.Services;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.DashboardView)]
    [ApiController]
    [Route("api/dashboard")]
    public class DashboardApiController : ControllerBase
    {
        private readonly DashboardService _service;

        public DashboardApiController(DashboardService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var data = await _service.GetDashboardAsync(User);
            return Ok(data);
        }
    }
}

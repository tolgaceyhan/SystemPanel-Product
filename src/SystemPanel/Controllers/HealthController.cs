using SystemPanel.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SystemPanel.Services;

namespace SystemPanel.Controllers
{
    [ApiController]
    [Route("api/health")]
    [RequirePermission(Permissions.SettingsView)]
    public class HealthController : ControllerBase
    {
        private readonly LdapProbeService _probe;
        private readonly ILogger<HealthController> _logger;

        public HealthController(LdapProbeService probe, ILogger<HealthController> logger)
        {
            _probe = probe;
            _logger = logger;
        }

        [HttpGet("ldap")]
        public async Task<IActionResult> Ldap(CancellationToken ct)
        {
            var ok = await _probe.PingAsync(ct);
            if (ok) return Ok(new { status = "ok" });
            return StatusCode(503, new { status = "fail" });
        }
        // /Health
        [HttpGet]
        public IActionResult Index() => Content("Health endpoint is active.");
    }
}

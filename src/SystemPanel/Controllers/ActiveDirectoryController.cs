using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Security;
using SystemPanel.Security.Scope;
using SystemPanel.Services;
using SystemPanel.Services.ActiveDirectory;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.AdUsersView)]
    [Route("api/ad")]
    public class ActiveDirectoryController : ControllerBase
    {
        private readonly IAdUserOperations _ops;
        private readonly IAdUserService _ad;
        private readonly IOrganizationalScopeResolver _scopes;
        private readonly ILogger<ActiveDirectoryController> _logger;

        public ActiveDirectoryController(IAdUserOperations ops, IAdUserService ad, IOrganizationalScopeResolver scopes, ILogger<ActiveDirectoryController> logger)
        {
            _ops = ops;
            _ad = ad;
            _scopes = scopes;
            _logger = logger;
        }

        // Kullanıcı arama (kapsam servis katmanında uygulanır). Terim servis içinde LDAP-escape edilir;
        // önceden burada da escape edildiği için '*', '(' içeren aramalar bozuluyordu.
        [HttpGet("users")]
        public async Task<IActionResult> SearchUsers([FromQuery] string term, [FromQuery] string? provinceOrDn, CancellationToken ct)
            => Ok(await _ops.SearchAsync(User, term, provinceOrDn, ct));

        // LocalAdmin kapsam özeti (kullanıcı + bilgisayar sayısı)
        [HttpGet("local-admin-scope")]
        public async Task<IActionResult> GetLocalAdminScope([FromQuery] string ou, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ou))
                return BadRequest("ou parametresi zorunludur.");

            var scope = await _scopes.ResolveAsync(User, ct);
            var ouDn = _ad.BuildOuDn(ou);
            if (!scope.Contains(ouDn))
                return Forbid();

            return Ok(await _ad.GetLocalAdminScopeAsync(ouDn, ct));
        }

        // OU ağacı (global olmayan kullanıcılar yalnızca kendi alt ağaçlarını görür)
        [HttpGet("ou-tree")]
        public async Task<IActionResult> GetOuTree(CancellationToken ct)
        {
            var tree = await _ops.GetOuTreeAsync(User, ct);
            return tree == null ? Forbid() : Ok(tree);
        }
    }
}

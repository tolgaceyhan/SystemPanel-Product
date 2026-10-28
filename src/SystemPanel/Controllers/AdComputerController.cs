using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Security;
using SystemPanel.Services;
using SystemPanel.Services.ActiveDirectory;
using SystemPanel.ViewModels.AdComputer;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.AdComputersView)]
    [Route("ad-computers")]
    public class AdComputerController : Controller
    {
        private readonly IAdComputerOperations _ops;
        private readonly IAdComputerService _svc;
        private readonly ILogger<AdComputerController> _logger;

        public AdComputerController(IAdComputerOperations ops, IAdComputerService svc, ILogger<AdComputerController> logger)
        {
            _ops = ops;
            _svc = svc;
            _logger = logger;
        }

        // GET /ad-computers  (tablo AJAX ile dolar)
        [HttpGet("")]
        public IActionResult Index() => View();

        // GET /ad-computers/list?searchTerm=&page=&pageSize=&sortColumn=&sortDir=
        [HttpGet("list")]
        public async Task<IActionResult> List(
            string? searchTerm,
            int page = 1,
            int pageSize = 50,
            string sortColumn = "Name",
            string sortDir = "asc",
            CancellationToken ct = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 500);
            sortColumn = string.IsNullOrWhiteSpace(sortColumn) ? "Name" : sortColumn;
            sortDir = sortDir?.ToLowerInvariant() == "desc" ? "desc" : "asc";

            // Kapsam (il/birim) servis katmanında uygulanır.
            var all = await _ops.SearchAsync(User, searchTerm, ct);

            IOrderedEnumerable<Models.AdComputer> ordered = sortColumn switch
            {
                "OperatingSystem" => sortDir == "desc" ? all.OrderByDescending(x => x.OperatingSystem) : all.OrderBy(x => x.OperatingSystem),
                "LastLogon" => sortDir == "desc" ? all.OrderByDescending(x => x.LastLogon) : all.OrderBy(x => x.LastLogon),
                "Status" => sortDir == "desc" ? all.OrderByDescending(x => x.IsEnabled) : all.OrderBy(x => x.IsEnabled),
                _ => sortDir == "desc" ? all.OrderByDescending(x => x.Name) : all.OrderBy(x => x.Name),
            };

            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToViewModel)
                .ToList();

            return Ok(new { items, total = all.Count, page, pageSize, sortColumn, sortDir });
        }

        // GET /ad-computers/details?name=SRV-01   (LAPS parolası İÇERMEZ)
        [HttpGet("details")]
        public async Task<IActionResult> Details([FromQuery] string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("name zorunludur.");

            var comp = await _ops.GetAsync(User, name, ct);
            return comp == null ? NotFound() : Ok(ToViewModel(comp));
        }

        // POST /ad-computers/laps  → yalnızca istek üzerine, laps.read + kapsam + audit
        [HttpPost("laps")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.LapsRead)]
        public async Task<IActionResult> RevealLaps([FromForm] string name, CancellationToken ct = default)
        {
            var (result, laps) = await _ops.RevealLapsAsync(User, name, ct);

            Response.Headers.CacheControl = "no-store";

            if (result.Success && laps != null)
            {
                return Ok(new
                {
                    password = laps.Password,
                    source = laps.Source.ToString(),
                    accountName = laps.AccountName,
                    expiresUtc = laps.ExpiresUtc,
                });
            }

            return StatusCode(result.StatusCode, new { message = laps?.Message ?? result.Error });
        }

        // GET /ad-computers/server-names (autocomplete)
        [HttpGet("server-names")]
        [RequirePermission(Permissions.ServersView)]
        public async Task<IActionResult> GetServerNames(CancellationToken ct = default)
            => Ok(await _svc.GetAllServerNamesAsync(ct));

        private static AdComputerViewModel ToViewModel(Models.AdComputer c) => new()
        {
            Name = c.Name,
            DistinguishedName = c.DistinguishedName,
            OperatingSystem = c.OperatingSystem,
            Description = c.Description,
            LastLogonDate = c.LastLogon,
            Status = c.IsEnabled ? "Aktif" : "Pasif",
        };
    }
}

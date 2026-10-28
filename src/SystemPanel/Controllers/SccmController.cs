using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Auditing;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Security.Scope;
using SystemPanel.Services;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.SccmView)]
    [Route("Sccm")]
    public class SccmController : Controller
    {
        private static readonly Regex HostName = new(@"^[A-Za-z0-9][A-Za-z0-9\-\.]{0,252}$", RegexOptions.Compiled);

        private readonly SccmSnapshotService _snapshot;
        private readonly SccmLogService _sccm;
        private readonly IOrganizationalScopeResolver _scope;
        private readonly IAuditService _audit;
        private readonly SccmOptions _options;
        private readonly ILogger<SccmController> _logger;

        public SccmController(
            SccmSnapshotService snapshot,
            SccmLogService sccm,
            IOrganizationalScopeResolver scope,
            IAuditService audit,
            IOptions<SccmOptions> options,
            ILogger<SccmController> logger)
        {
            _snapshot = snapshot;
            _sccm = sccm;
            _scope = scope;
            _audit = audit;
            _options = options.Value;
            _logger = logger;
        }

        // =========================
        // DP LİSTESİ (SNAPSHOT)
        // =========================
        [HttpGet("Dps")]
        public async Task<IActionResult> Dps(CancellationToken ct)
            => View(await GetVisibleDpsAsync(ct));

        // =========================
        // DP DETAIL (CANLI WMI)
        // =========================
        [HttpGet("Details")]
        public async Task<IActionResult> Details(string serverName, CancellationToken ct)
        {
            if (!await IsVisibleServerAsync(serverName, ct))
                return NotFound();

            var dp = _sccm.GetDpDetails(serverName);
            return View(dp);
        }

        // =========================
        // SERVICE CONTROL
        // =========================
        [HttpPost("StartService")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.SccmManage)]
        public Task<IActionResult> StartService(string serverName, string serviceName, CancellationToken ct)
            => ControlAsync(serverName, serviceName, ServiceAction.Start, ct);

        [HttpPost("StopService")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.SccmManage)]
        public Task<IActionResult> StopService(string serverName, string serviceName, CancellationToken ct)
            => ControlAsync(serverName, serviceName, ServiceAction.Stop, ct);

        [HttpPost("RestartService")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.SccmManage)]
        public Task<IActionResult> RestartService(string serverName, string serviceName, CancellationToken ct)
            => ControlAsync(serverName, serviceName, ServiceAction.Restart, ct);

        private enum ServiceAction { Start, Stop, Restart }

        private async Task<IActionResult> ControlAsync(string serverName, string serviceName, ServiceAction action, CancellationToken ct)
        {
            var auditAction = action switch
            {
                ServiceAction.Start => AuditActions.ServiceStart,
                ServiceAction.Stop => AuditActions.ServiceStop,
                _ => AuditActions.ServiceRestart,
            };

            // Yalnızca yapılandırmada izin verilen servisler (önceden serbest metin ve WQL'e gömülüyordu).
            var service = _options.ControllableServices.FirstOrDefault(s => string.Equals(s, serviceName, StringComparison.OrdinalIgnoreCase));
            if (service == null)
                return await DenyAsync(auditAction, serverName, serviceName, "Bu servis için uzaktan kontrol izni yok.");

            // Sunucu → il eşlemesi henüz olmadığından uzaktan servis kontrolü organizasyon geneli
            // kapsam (scope.all) gerektirir; LocalAdmin için agent komut modeli gelecektir (ADR-001).
            var scope = await _scope.ResolveAsync(User, ct);
            if (!scope.IsGlobal)
                return await DenyAsync(auditAction, serverName, service, "Uzaktan servis kontrolü için organizasyon geneli yetki gerekir.");

            if (!await IsVisibleServerAsync(serverName, ct))
                return await DenyAsync(auditAction, serverName, service, "Sunucu SCCM DP listesinde bulunamadı.");

            try
            {
                if (action == ServiceAction.Restart)
                {
                    _sccm.ControlService(serverName, service, start: false);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    _sccm.ControlService(serverName, service, start: true);
                }
                else
                {
                    _sccm.ControlService(serverName, service, start: action == ServiceAction.Start);
                }

                await _audit.RecordAsync(new AuditRecord(auditAction)
                {
                    ResourceType = "WindowsService",
                    ResourceId = $"{serverName}/{service}",
                    ResourceName = service,
                    Scope = scope.Describe(),
                    Metadata = new { Server = serverName, Service = service },
                }, ct);

                return Json(new { success = true, message = $"{service}: {action} komutu gönderildi." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Servis kontrolü başarısız: {Server}/{Service} {Action}", serverName, service, action);
                await _audit.RecordAsync(new AuditRecord(auditAction)
                {
                    ResourceType = "WindowsService",
                    ResourceId = $"{serverName}/{service}",
                    ResourceName = service,
                    Success = false,
                    ErrorCode = ex.GetType().Name,
                    ErrorMessage = "Servis kontrolü başarısız.",
                }, ct);
                return StatusCode(502, new { success = false, message = "Servis kontrolü başarısız. Ayrıntı uygulama logunda." });
            }
        }

        private async Task<IActionResult> DenyAsync(string auditAction, string? server, string? service, string reason)
        {
            await _audit.RecordAsync(new AuditRecord(auditAction)
            {
                ResourceType = "WindowsService",
                ResourceId = $"{server}/{service}",
                Success = false,
                ErrorCode = "denied",
                ErrorMessage = reason,
            });
            return StatusCode(403, new { success = false, message = reason });
        }

        private async Task<List<SccmDpSnapshot>> GetVisibleDpsAsync(CancellationToken ct)
        {
            var dps = _snapshot.GetDpSnapshots();

            var scope = await _scope.ResolveAsync(User, ct);
            if (scope.IsGlobal)
                return dps;

            // LocalAdmin: il (sp:province) claim'leri ile BoundaryGroupName eşleşmesi.
            var provinces = User.FindAll(ScopeClaimTypes.Province)
                .Select(x => x.Value.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (provinces.Count == 0)
                return new List<SccmDpSnapshot>();

            return dps.Where(dp =>
                !string.IsNullOrWhiteSpace(dp.BoundaryGroupName) &&
                provinces.Any(p => dp.BoundaryGroupName.Contains("-" + p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private async Task<bool> IsVisibleServerAsync(string? serverName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(serverName) || !HostName.IsMatch(serverName))
                return false;

            var visible = await GetVisibleDpsAsync(ct);
            return visible.Any(dp => string.Equals(dp.ServerName, serverName, StringComparison.OrdinalIgnoreCase));
        }
    }
}

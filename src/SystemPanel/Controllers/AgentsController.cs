using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SystemPanel.Security;
using SystemPanel.Services.Agents;

namespace SystemPanel.Controllers
{
    /// <summary>Agent kaydı / anahtar yönetimi (monitoring.manage).</summary>
    [RequirePermission(Permissions.MonitoringManage)]
    public class AgentsController : Controller
    {
        private readonly AgentRegistry _registry;
        private readonly MonitoringOptions _options;

        public AgentsController(AgentRegistry registry, IOptions<MonitoringOptions> options)
        {
            _registry = registry;
            _options = options.Value;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            ViewBag.OfflineAfterSeconds = _options.AgentOfflineAfterSeconds;
            ViewBag.LegacyIngestEnabled = _options.AllowLegacyAnonymousIngest;
            return View(await _registry.ListAsync(ct));
        }

        [HttpGet]
        public IActionResult Register() => View(new AgentRegisterViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(AgentRegisterViewModel model, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var credentials = await _registry.RegisterAsync(model.Hostname, model.Province, model.Description, User.Identity?.Name, ct);
                return CredentialsView(credentials, rotated: false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                ModelState.AddModelError(nameof(model.Hostname), ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RotateKey(Guid id, CancellationToken ct)
        {
            var credentials = await _registry.RotateKeyAsync(id, ct);
            return credentials == null ? NotFound() : CredentialsView(credentials, rotated: true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetEnabled(Guid id, bool enabled, CancellationToken ct)
        {
            try
            {
                if (!await _registry.SetEnabledAsync(id, enabled, ct))
                    return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = enabled ? "Agent etkinleştirildi." : "Agent devre dışı bırakıldı; gönderdiği veriler reddedilecek.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProvince(Guid id, string? province, CancellationToken ct)
        {
            if (!await _registry.UpdateProvinceAsync(id, province, ct))
                return NotFound();

            TempData["Success"] = "İl bilgisi güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Anahtar YALNIZCA bu yanıtta gösterilir: TempData'ya (cookie) yazılmaz, önbelleğe alınmaz,
        /// redirect yapılmaz. Sayfa kapatıldıktan sonra anahtar kurtarılamaz; yenilemek gerekir.
        /// </summary>
        private IActionResult CredentialsView(AgentCredentials credentials, bool rotated)
        {
            Response.Headers.CacheControl = "no-store, no-cache";
            Response.Headers.Pragma = "no-cache";
            ViewBag.Rotated = rotated;
            ViewBag.ServerUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            return View("Credentials", credentials);
        }
    }

    public class AgentRegisterViewModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Sunucu adı zorunludur.")]
        [System.ComponentModel.DataAnnotations.StringLength(63)]
        public string Hostname { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string? Province { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(256)]
        public string? Description { get; set; }
    }
}

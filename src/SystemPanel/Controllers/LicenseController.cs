using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SystemPanel.Auditing;
using SystemPanel.Contracts.Licensing;
using SystemPanel.Licensing;
using SystemPanel.Security;

namespace SystemPanel.Controllers
{
    /// <summary>Ürün lisansı: durum görüntüleme (settings.view) ve lisans dosyası yükleme (settings.manage).</summary>
    [RequirePermission(Permissions.SettingsView)]
    public class LicenseController : Controller
    {
        private readonly ILicenseService _license;
        private readonly IAuditService _audit;

        public LicenseController(ILicenseService license, IAuditService audit)
        {
            _license = license;
            _audit = audit;
        }

        [HttpGet]
        public IActionResult Index() => View(_license.GetStatus());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.SettingsManage)]
        [RequestSizeLimit(LicenseCodec.MaxFileBytes + 16 * 1024)]
        public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
        {
            if (file == null || file.Length == 0 || file.Length > LicenseCodec.MaxFileBytes)
            {
                TempData["Error"] = "Geçerli bir lisans dosyası seçin.";
                return RedirectToAction(nameof(Index));
            }

            string text;
            using (var reader = new StreamReader(file.OpenReadStream()))
                text = await reader.ReadToEndAsync(ct);

            var status = await _license.InstallAsync(text, ct);
            var ok = status.License != null && status.State != LicenseState.Invalid;

            await _audit.RecordAsync(new AuditRecord(AuditActions.LicenseInstall)
            {
                ResourceType = "License",
                ResourceId = status.License?.LicenseId,
                ResourceName = status.License?.Customer,
                Success = ok,
                ErrorMessage = ok ? null : status.Message,
                After = status.License == null ? null : new { status.License.Edition, status.License.ExpiresUtc, status.License.MaxAgents },
            }, ct);

            TempData[ok ? "Success" : "Error"] = ok ? "Lisans kuruldu." : status.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}

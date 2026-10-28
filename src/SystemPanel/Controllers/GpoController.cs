using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Auditing;
using SystemPanel.Security;
using SystemPanel.Services;
using SystemPanel.Services.Gpo;
using SystemPanel.ViewModels.Gpo;

namespace SystemPanel.Controllers
{
    /// <summary>
    /// GPO envanteri ve kontrollü işlemler. Değiştiren tüm işlemler POST + antiforgery +
    /// ayrı izin + audit gerektirir. Silme ve içe aktarma yapılandırmayla açılmadıkça kapalıdır.
    /// </summary>
    [RequirePermission(Permissions.GpoView)]
    public class GpoController : Controller
    {
        private readonly IGpoService _svc;
        private readonly IAdUserService _ad;
        private readonly IAuditService _audit;
        private readonly GpoOptions _options;
        private readonly ILogger<GpoController> _logger;

        public GpoController(
            IGpoService svc,
            IAdUserService ad,
            IAuditService audit,
            IOptions<GpoOptions> options,
            ILogger<GpoController> logger)
        {
            _svc = svc;
            _ad = ad;
            _audit = audit;
            _options = options.Value;
            _logger = logger;
        }

        // ============= LIST =============
        [HttpGet]
        public async Task<IActionResult> Index(string? ou = null, CancellationToken ct = default)
        {
            var list = string.IsNullOrWhiteSpace(ou)
                ? await _svc.GetAllGposAsync(ct)
                : await _svc.GetGposByOuAsync(ou, ct);

            ViewBag.SelectedOu = ou;
            ViewBag.AllowDelete = _options.AllowDelete;
            return View(list);
        }

        // ============= DETAILS =============
        [HttpGet]
        public async Task<IActionResult> Details(string id, CancellationToken ct = default)
        {
            var gpo = await _svc.GetGpoByIdAsync(id, ct);
            if (gpo == null)
                return NotFound();

            ViewBag.AllowDelete = _options.AllowDelete;
            return View(gpo);
        }

        // ============= CREATE (henüz desteklenmiyor) =============
        [HttpGet]
        [RequirePermission(Permissions.GpoCreate)]
        public IActionResult Create()
        {
            return View(new CreateGpoViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoCreate)]
        public IActionResult Create(CreateGpoViewModel model)
        {
            // Önceki sürüm NotImplementedException (HTTP 500) fırlatıyordu. GPO oluşturma ROADMAP faz 3'tedir.
            TempData["Error"] = "GPO oluşturma bu sürümde desteklenmiyor.";
            return View(model);
        }

        // ============= LINK TO OU =============
        [HttpGet]
        [RequirePermission(Permissions.GpoLink)]
        public async Task<IActionResult> LinkToOu(string id, CancellationToken ct = default)
        {
            var gpo = await _svc.GetGpoByIdAsync(id, ct);
            if (gpo == null)
                return NotFound();

            await LoadOuListAsync(ct);
            return View(new LinkToOuViewModel { GpoId = gpo.Id, GpoName = gpo.DisplayName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoLink)]
        public async Task<IActionResult> LinkToOu(LinkToOuViewModel model, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                await LoadOuListAsync(ct);
                return View(model);
            }

            var result = await _svc.LinkGpoToOuAsync(model.GpoId, model.OrganizationalUnit, ct);
            await AuditAsync(AuditActions.GpoLink, model.GpoId, model.GpoName, result, new { TargetOu = model.OrganizationalUnit });

            SetResultMessage(result, "GPO OU'ya bağlandı.");
            return RedirectToAction(nameof(Details), new { id = model.GpoId });
        }

        // ============= UNLINK =============
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoLink)]
        public async Task<IActionResult> Unlink(string id, string ouDn, CancellationToken ct = default)
        {
            var result = await _svc.UnlinkGpoFromOuAsync(id, ouDn, ct);
            await AuditAsync(AuditActions.GpoUnlink, id, null, result, new { TargetOu = ouDn });

            SetResultMessage(result, "GPO bağlantısı kaldırıldı.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // ============= ENABLE/DISABLE =============
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoEdit)]
        public async Task<IActionResult> Toggle(string id, bool enable, CancellationToken ct = default)
        {
            var result = await _svc.SetGpoEnabledAsync(id, enable, ct);
            await AuditAsync(AuditActions.GpoStatusChange, id, null, result, new { Enabled = enable });

            SetResultMessage(result, "GPO durumu güncellendi.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // ============= DELETE =============
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoDelete)]
        public async Task<IActionResult> Delete(string id, string? confirmName, CancellationToken ct = default)
        {
            var gpo = await _svc.GetGpoByIdAsync(id, ct);
            if (gpo == null)
                return NotFound();

            // Yanlışlıkla silmeye karşı: kullanıcı GPO adını yazarak onaylamalıdır.
            if (!string.Equals(confirmName?.Trim(), gpo.DisplayName, System.StringComparison.Ordinal))
            {
                TempData["Error"] = "Silme onayı için GPO adını birebir yazmalısınız.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var result = await _svc.DeleteGpoAsync(id, ct);
            await AuditAsync(AuditActions.GpoDelete, gpo.Id, gpo.DisplayName, result, new { gpo.LinkedOus });

            SetResultMessage(result, "GPO silindi.");
            return result.Success ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id });
        }

        // ============= BACKUP =============
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoBackup)]
        public async Task<IActionResult> Backup(string id, CancellationToken ct = default)
        {
            var result = await _svc.BackupGpoAsync(id, ct);
            await AuditAsync(AuditActions.GpoBackup, id, null, result, new { BackupPath = result.Detail });

            SetResultMessage(result, $"GPO yedeklendi: {result.Detail}");
            return RedirectToAction(nameof(Details), new { id });
        }

        // ============= IMPORT =============
        [HttpGet]
        [RequirePermission(Permissions.GpoCreate)]
        public IActionResult Import()
        {
            ViewBag.AllowImport = _options.AllowImport;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.GpoCreate)]
        [RequestSizeLimit(60 * 1024 * 1024)]
        public async Task<IActionResult> Import(IFormFile backupFile, string backupGpoName, string targetName, CancellationToken ct = default)
        {
            var result = await _svc.ImportGpoAsync(backupFile, backupGpoName, targetName, ct);
            await AuditAsync(AuditActions.GpoImport, null, targetName, result,
                new { BackupGpoName = backupGpoName, FileName = backupFile?.FileName, Size = backupFile?.Length });

            SetResultMessage(result, "GPO içe aktarıldı.");
            return RedirectToAction(nameof(Index));
        }

        // ============= OU TREE =============
        [HttpGet]
        public async Task<IActionResult> Tree()
        {
            var tree = await _svc.GetOuGpoTreeAsync();
            return View(tree);
        }

        // ============= YARDIMCILAR =============
        private async Task LoadOuListAsync(CancellationToken ct)
        {
            var ous = await _ad.GetOrganizationalUnitsByNameAsync(string.Empty, ct);
            ViewBag.OuList = ous.Select(o => o.DistinguishedName).OrderBy(x => x).ToList();
        }

        private void SetResultMessage(GpoOperationResult result, string successMessage)
        {
            TempData[result.Success ? "Success" : "Error"] = result.Success ? successMessage : result.Error;
        }

        private Task AuditAsync(string action, string? gpoId, string? gpoName, GpoOperationResult result, object? metadata)
            => _audit.RecordAsync(new AuditRecord(action)
            {
                ResourceType = "Gpo",
                ResourceId = gpoId,
                ResourceName = gpoName,
                Success = result.Success,
                ErrorMessage = result.Error,
                Metadata = metadata,
            });
    }
}

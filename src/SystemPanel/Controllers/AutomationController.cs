using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SystemPanel.Automation;
using SystemPanel.Contracts.Automation;
using SystemPanel.Models;
using SystemPanel.Security;

namespace SystemPanel.Controllers
{
    /// <summary>
    /// Otomasyon (ADR-002): katalog, çalıştırma talebi, onay, sonuçlar ve katalog/anahtar yönetimi.
    /// Kurallar (dört göz, kapsam, imza) <see cref="RunbookRunService"/> içindedir; burası yalnızca arayüzdür.
    /// </summary>
    [RequirePermission(Permissions.AutomationView)]
    [Route("automation")]
    public class AutomationController : Controller
    {
        private const string ParameterPrefix = "p.";

        private readonly RunbookCatalog _catalog;
        private readonly RunbookRunService _runs;
        private readonly RunbookTrustStore _trust;
        private readonly RunbookScheduleService _schedules;

        public AutomationController(RunbookCatalog catalog, RunbookRunService runs, RunbookTrustStore trust, RunbookScheduleService schedules)
        {
            _catalog = catalog;
            _runs = runs;
            _trust = trust;
            _schedules = schedules;
        }

        // ------------------------------------------------------------------ Katalog
        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            ViewBag.AwaitingDecision = (await _runs.ListAwaitingDecisionAsync(User, ct)).Count;
            return View(await _catalog.ListLatestAsync(ct));
        }

        // ------------------------------------------------------------------ Talep
        [HttpGet("runbooks/{versionId:guid}/request")]
        [RequirePermission(Permissions.AutomationRun)]
        public async Task<IActionResult> NewRun(Guid versionId, CancellationToken ct)
        {
            var model = await BuildRequestModelAsync(versionId, ct);
            return model == null ? NotFound() : View(model);
        }

        [HttpPost("runbooks/{versionId:guid}/request")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationRun)]
        public async Task<IActionResult> NewRun(Guid versionId, string? justification, Guid? targetAgentId, CancellationToken ct)
        {
            var model = await BuildRequestModelAsync(versionId, ct);
            if (model == null)
                return NotFound();

            var input = Request.Form
                .Where(kv => kv.Key.StartsWith(ParameterPrefix, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key[ParameterPrefix.Length..], kv => (string?)kv.Value.LastOrDefault(), StringComparer.Ordinal);

            var result = await _runs.RequestAsync(User, versionId, input, justification, targetAgentId, ct);
            if (!result.Success)
            {
                model.Values = input;
                model.Justification = justification;
                model.TargetAgentId = targetAgentId;
                model.Errors = result.ValidationErrors?.ToList() ?? new List<string>();
                if (result.Error != null && model.Errors.Count == 0)
                    model.Errors.Add(result.Error);
                return View(model);
            }

            TempData["Success"] = result.Run!.Status switch
            {
                RunbookRunStatus.PendingApproval => "Talep oluşturuldu; ikinci bir yetkilinin onayı bekleniyor.",
                RunbookRunStatus.Approved => "Talep kuyruğa alındı.",
                RunbookRunStatus.PreviewPending => "Talep oluşturuldu; önizleme hedef sunucudaki agent'ta çalışacak.",
                _ => null,
            };
            if (result.Run.Status == RunbookRunStatus.PreviewFailed)
                TempData["Error"] = "Önizleme başarısız oldu; talep çalıştırılmayacak. Ayrıntılar aşağıda.";
            return RedirectToAction(nameof(Run), new { id = result.Run.Id });
        }

        private async Task<RunbookRequestViewModel?> BuildRequestModelAsync(Guid versionId, CancellationToken ct)
        {
            var version = await _catalog.FindAsync(versionId, ct);
            if (version == null || !version.IsEnabled)
                return null;

            var (package, error) = await _catalog.VerifyAsync(version, ct);
            return new RunbookRequestViewModel
            {
                Version = version,
                Manifest = package?.Manifest,
                VerificationError = error,
                TargetAgents = package?.Manifest.Executor == RunbookExecutors.Agent
                    ? await _runs.ListTargetAgentsAsync(User, ct)
                    : Array.Empty<Agent>(),
            };
        }

        // ------------------------------------------------------------------ Çalıştırmalar
        [HttpGet("runs")]
        public async Task<IActionResult> Runs(string? status, CancellationToken ct)
        {
            ViewBag.Status = status;
            return View(await _runs.ListVisibleAsync(User, status, 200, ct));
        }

        [HttpGet("approvals")]
        [RequirePermission(Permissions.AutomationApprove)]
        public async Task<IActionResult> Approvals(CancellationToken ct)
            => View("Runs", await _runs.ListAwaitingDecisionAsync(User, ct));

        [HttpGet("runs/{id:guid}")]
        public async Task<IActionResult> Run(Guid id, CancellationToken ct)
        {
            var run = await _runs.LoadAsync(id, ct);
            if (run == null || !await _runs.CanViewAsync(User, run, ct))
                return NotFound();

            ViewBag.Parameters = RunbookRunService.DescribeParameters(run.ParametersJson);
            ViewBag.CanDecide = (await _runs.ListAwaitingDecisionAsync(User, ct)).Any(r => r.Id == run.Id);
            ViewBag.TargetAgent = run.TargetAgentId == null ? null : run.TargetSummary;
            ViewBag.CanCancel = run.Status is RunbookRunStatus.PendingApproval or RunbookRunStatus.Approved or RunbookRunStatus.PreviewPending
                && string.Equals(run.RequestedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase);
            return View(run);
        }

        [HttpPost("runs/{id:guid}/approve")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationApprove)]
        public async Task<IActionResult> Approve(Guid id, string? comment, CancellationToken ct)
            => Decided(id, await _runs.ApproveAsync(User, id, comment, ct), "Onaylandı.");

        [HttpPost("runs/{id:guid}/reject")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationApprove)]
        public async Task<IActionResult> Reject(Guid id, string? comment, CancellationToken ct)
            => Decided(id, await _runs.RejectAsync(User, id, comment, ct), "Reddedildi.");

        [HttpPost("runs/{id:guid}/cancel")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationRun)]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
            => Decided(id, await _runs.CancelAsync(User, id, ct), "İptal edildi.");

        private IActionResult Decided(Guid id, RunbookOperationResult result, string success)
        {
            TempData[result.Success ? "Success" : "Error"] = result.Success ? success : result.Error;
            return RedirectToAction(nameof(Run), new { id });
        }

        // ------------------------------------------------------------------ Zamanlamalar (ADR-002 §5)
        [HttpGet("schedules")]
        public async Task<IActionResult> Schedules(CancellationToken ct)
            => View(await _schedules.ListVisibleAsync(User, ct));

        [HttpGet("runbooks/{versionId:guid}/schedule")]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> NewSchedule(Guid versionId, CancellationToken ct)
        {
            var request = await BuildRequestModelAsync(versionId, ct);
            return request == null ? NotFound() : View(new ScheduleFormViewModel { Request = request });
        }

        [HttpPost("runbooks/{versionId:guid}/schedule")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> NewSchedule(Guid versionId, ScheduleFormViewModel form, Guid? targetAgentId, string? justification, CancellationToken ct)
        {
            var request = await BuildRequestModelAsync(versionId, ct);
            if (request == null)
                return NotFound();

            var input = Request.Form
                .Where(kv => kv.Key.StartsWith(ParameterPrefix, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key[ParameterPrefix.Length..], kv => (string?)kv.Value.LastOrDefault(), StringComparer.Ordinal);

            var result = await _schedules.CreateAsync(User, new ScheduleRequest
            {
                Name = form.Name ?? string.Empty,
                VersionId = versionId,
                Input = input,
                TargetAgentId = targetAgentId,
                TriggerType = form.TriggerType ?? string.Empty,
                CronExpression = form.CronExpression,
                AlarmMetric = form.AlarmMetric,
                AlarmDrive = form.AlarmDrive,
                MinIntervalMinutes = form.MinIntervalMinutes,
                Justification = justification,
            }, ct);

            if (!result.Success)
            {
                request.Values = input;
                request.Justification = justification;
                request.TargetAgentId = targetAgentId;
                request.Errors = result.ValidationErrors?.ToList() ?? new List<string>();
                if (result.Error != null && request.Errors.Count == 0)
                    request.Errors.Add(result.Error);
                form.Request = request;
                return View(form);
            }

            TempData["Success"] = result.Schedule!.Status == RunbookScheduleStatus.Active
                ? "Zamanlama oluşturuldu ve etkin."
                : "Zamanlama oluşturuldu; etkin olması için başka bir yetkilinin onayı gerekiyor.";
            return RedirectToAction(nameof(Schedule), new { id = result.Schedule.Id });
        }

        [HttpGet("schedules/{id:guid}")]
        public async Task<IActionResult> Schedule(Guid id, CancellationToken ct)
        {
            var schedule = await _schedules.FindVisibleAsync(User, id, ct);
            if (schedule == null)
                return NotFound();

            ViewBag.Parameters = RunbookRunService.DescribeParameters(schedule.ParametersJson);
            ViewBag.Runs = (await _runs.ListVisibleAsync(User, null, 500, ct)).Where(r => r.ScheduleId == id).Take(20).ToList();
            return View(schedule);
        }

        [HttpPost("schedules/{id:guid}/approve")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationApprove)]
        public async Task<IActionResult> ApproveSchedule(Guid id, CancellationToken ct)
            => ScheduleDecided(id, await _schedules.ApproveAsync(User, id, ct), "Zamanlama onaylandı ve etkinleşti.");

        [HttpPost("schedules/{id:guid}/reject")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationApprove)]
        public async Task<IActionResult> RejectSchedule(Guid id, CancellationToken ct)
            => ScheduleDecided(id, await _schedules.RejectAsync(User, id, ct), "Zamanlama reddedildi.");

        [HttpPost("schedules/{id:guid}/disable")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> DisableSchedule(Guid id, CancellationToken ct)
            => ScheduleDecided(id, await _schedules.DisableAsync(User, id, ct), "Zamanlama devre dışı bırakıldı.");

        private IActionResult ScheduleDecided(Guid id, ScheduleOperationResult result, string success)
        {
            TempData[result.Success ? "Success" : "Error"] = result.Success ? success : result.Error;
            return RedirectToAction(nameof(Schedule), new { id });
        }

        // ------------------------------------------------------------------ Yönetim
        [HttpGet("manage")]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> Manage(CancellationToken ct)
        {
            ViewBag.Keys = await _trust.ListAsync(ct);
            return View(await _catalog.ListAllAsync(ct));
        }

        [HttpPost("manage/import")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationManage)]
        [RequestSizeLimit(RunbookPackageCodec.MaxPackageBytes + 64 * 1024)]
        public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
        {
            if (file == null || file.Length == 0 || file.Length > RunbookPackageCodec.MaxPackageBytes)
            {
                TempData["Error"] = "Geçerli bir runbook paketi (.sprb) seçin.";
                return RedirectToAction(nameof(Manage));
            }

            string text;
            using (var reader = new StreamReader(file.OpenReadStream()))
                text = await reader.ReadToEndAsync(ct);

            var result = await _catalog.ImportAsync(text, User.Identity?.Name, ct);
            TempData[result.Success ? "Success" : "Error"] = result.Success
                ? (result.AlreadyPresent ? "Bu sürüm zaten katalogda." : $"İçe aktarıldı: {result.Version!.RunbookId}@{result.Version.Version}")
                : result.Error;
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost("manage/runbooks/{versionId:guid}/enabled")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> SetEnabled(Guid versionId, bool enabled, CancellationToken ct)
        {
            if (!await _catalog.SetEnabledAsync(versionId, enabled, ct))
                return NotFound();
            TempData["Success"] = enabled ? "Runbook sürümü etkinleştirildi." : "Runbook sürümü devre dışı bırakıldı.";
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost("manage/keys")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> AddKey(string? name, string? publicKeyPem, CancellationToken ct)
        {
            var (ok, error) = await _trust.AddAsync(name ?? string.Empty, publicKeyPem ?? string.Empty, User.Identity?.Name, ct);
            TempData[ok ? "Success" : "Error"] = ok ? "İmzalama anahtarı eklendi." : error;
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost("manage/keys/{id:guid}/revoke")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AutomationManage)]
        public async Task<IActionResult> RevokeKey(Guid id, [FromServices] RunbookKeyRevocationService revocation, CancellationToken ct)
        {
            var result = await revocation.RevokeAsync(id, User.Identity?.Name, ct);
            TempData[result.Success ? "Success" : "Error"] = result.Success
                ? $"Anahtar iptal edildi; bu anahtarla imzalı runbook'lar artık çalıştırılamaz. İptal edilen talep: {result.CancelledRuns}, kapatılan zamanlama: {result.DisabledSchedules}."
                : result.Error;
            return RedirectToAction(nameof(Manage));
        }
    }

    public sealed class ScheduleFormViewModel
    {
        public RunbookRequestViewModel Request { get; set; } = null!;
        public string? Name { get; set; }
        public string? TriggerType { get; set; } = RunbookTriggerTypes.Cron;
        public string? CronExpression { get; set; } = "0 3 * * *";
        public string? AlarmMetric { get; set; }
        public string? AlarmDrive { get; set; }
        public int MinIntervalMinutes { get; set; } = 60;
    }

    public sealed class RunbookRequestViewModel
    {
        public RunbookVersion Version { get; set; } = null!;
        public RunbookManifest? Manifest { get; set; }
        public string? VerificationError { get; set; }
        public IReadOnlyDictionary<string, string?> Values { get; set; } = new Dictionary<string, string?>();
        public string? Justification { get; set; }
        public List<string> Errors { get; set; } = new();

        /// <summary>Agent runbook'ları için seçilebilir hedefler (kullanıcının kapsamındaki etkin agent'lar).</summary>
        public IReadOnlyList<Agent> TargetAgents { get; set; } = Array.Empty<Agent>();
        public Guid? TargetAgentId { get; set; }
    }
}

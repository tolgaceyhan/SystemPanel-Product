using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Auditing;
using SystemPanel.Contracts.Agents;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Security.Scope;

namespace SystemPanel.Automation
{
    public sealed record RunbookOperationResult(bool Success, string? Error, RunbookRun? Run = null, IReadOnlyList<string>? ValidationErrors = null)
    {
        public static RunbookOperationResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Runbook çalıştırma iş akışı (ADR-002 §3-4):
    /// talep → (önizleme) → onay (dört göz) → kuyruk → çalıştırma → sonuç. Tüm geçişler audit'lenir.
    /// Durum geçişleri YALNIZCA bu sınıfta yapılır.
    /// </summary>
    public sealed class RunbookRunService
    {
        private const int MaxPreviewSeconds = 120;

        private readonly ApplicationDbContext _db;
        private readonly RunbookCatalog _catalog;
        private readonly IOrganizationalScopeResolver _scopes;
        private readonly IPermissionService _permissions;
        private readonly IRunbookScriptHost _host;
        private readonly IAuditService _audit;
        private readonly AutomationOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<RunbookRunService> _logger;

        public RunbookRunService(
            ApplicationDbContext db,
            RunbookCatalog catalog,
            IOrganizationalScopeResolver scopes,
            IPermissionService permissions,
            IRunbookScriptHost host,
            IAuditService audit,
            IOptions<AutomationOptions> options,
            ILogger<RunbookRunService> logger,
            TimeProvider? time = null)
        {
            _db = db;
            _catalog = catalog;
            _scopes = scopes;
            _permissions = permissions;
            _host = host;
            _audit = audit;
            _options = options.Value;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        /// <summary>Risk düzeyine göre gereken onay sayısı (ADR-002 §3).</summary>
        public static int RequiredApprovalsFor(string risk) => risk switch
        {
            RunbookRisks.Low => 0,
            _ => 1,
        };

        // ------------------------------------------------------------------ Talep
        public async Task<RunbookOperationResult> RequestAsync(
            ClaimsPrincipal user,
            Guid versionId,
            IReadOnlyDictionary<string, string?> input,
            string? justification,
            Guid? targetAgentId = null,
            CancellationToken ct = default)
        {
            var requester = UserName(user);
            if (requester == null)
                return RunbookOperationResult.Fail("Kullanıcı belirlenemedi.");

            var version = await _db.RunbookVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version == null)
                return RunbookOperationResult.Fail("Runbook bulunamadı.");

            var (package, verifyError) = await _catalog.VerifyAsync(version, ct);
            if (package == null)
                return RunbookOperationResult.Fail(verifyError ?? "Runbook doğrulanamadı.");

            var scope = await _scopes.ResolveAsync(user, ct);
            var bound = RunbookParameterBinder.Bind(package.Manifest, input, scope);
            if (!bound.IsValid)
                return new RunbookOperationResult(false, "Parametreler geçersiz.", null, bound.Errors);

            if (package.Manifest.Risk != RunbookRisks.Low && string.IsNullOrWhiteSpace(justification))
                return new RunbookOperationResult(false, "Gerekçe gerekli.", null, new[] { "Orta/yüksek riskli runbook'lar için gerekçe yazın." });

            // Agent çalıştırıcısı: hedef agent kayıtlı, etkin ve kullanıcının kapsamında olmalı.
            var isAgent = package.Manifest.Executor == RunbookExecutors.Agent;
            Agent? targetAgent = null;
            if (isAgent)
            {
                targetAgent = targetAgentId == null ? null : await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetAgentId, ct);
                if (targetAgent == null || !targetAgent.IsEnabled)
                    return new RunbookOperationResult(false, "Hedef sunucu geçersiz.", null, new[] { "Kayıtlı ve etkin bir hedef sunucu (agent) seçin." });
                if (!AgentInScope(user, scope, targetAgent))
                    return new RunbookOperationResult(false, "Hedef sunucu kapsam dışında.", null, new[] { "Hedef sunucu yetki kapsamınızın dışında." });
            }
            else if (targetAgentId != null)
            {
                return RunbookOperationResult.Fail("Bu runbook bir hedef sunucu almaz.");
            }

            var run = await CreateRunAsync(version, package, bound, targetAgent, requester, justification,
                RequiredApprovalsFor(package.Manifest.Risk), scheduleId: null, actorType: null, ct);
            return new RunbookOperationResult(true, null, run);
        }

        /// <summary>
        /// Çalıştırma kaydını oluşturur (talep ve zamanlama ortak yolu): durum, önizleme ve audit.
        /// Parametreler, hedef ve yetki ÇAĞIRAN tarafından doğrulanmış olmalıdır.
        /// </summary>
        private async Task<RunbookRun> CreateRunAsync(
            RunbookVersion version,
            VerifiedRunbookPackage package,
            BoundRunbookParameters bound,
            Agent? targetAgent,
            string requester,
            string? justification,
            int required,
            Guid? scheduleId,
            string? actorType,
            CancellationToken ct)
        {
            var isAgent = targetAgent != null;
            var run = new RunbookRun
            {
                Id = Guid.NewGuid(),
                RunbookVersionId = version.Id,
                Status = required == 0 ? RunbookRunStatus.Approved : RunbookRunStatus.PendingApproval,
                ParametersJson = SerializeParameters(bound),
                TargetSummary = isAgent ? targetAgent!.Hostname : RunbookParameterBinder.Summarize(package.Manifest, bound),
                TargetAgentId = targetAgent?.Id,
                Justification = Truncate(justification?.Trim(), 500),
                RequestedBy = requester,
                RequestedAtUtc = Now,
                RequiredApprovals = required,
                ApprovalExpiresAtUtc = required == 0 ? null : Now.AddHours(Math.Clamp(_options.ApprovalExpiryHours, 1, 24 * 14)),
                CorrelationId = System.Diagnostics.Activity.Current?.Id is { } aid ? Truncate(aid, 64) : null,
                ScheduleId = scheduleId,
            };

            if (isAgent)
            {
                // Agent işleri agent'ın almasını bekler; alınmazsa (agent kapalı) son tarihte Expired olur.
                // Önizleme agent'ta çalışır; sonucu geldikten sonra onaya düşer (CompleteAgentCommandAsync).
                run.ApprovalExpiresAtUtc = Now.AddHours(Math.Clamp(_options.ApprovalExpiryHours, 1, 24 * 14));
                if (package.Manifest.SupportsDryRun)
                    run.Status = RunbookRunStatus.PreviewPending;
            }
            // Önizleme: değişiklik yapmadan planı gösterir. Yüksek riskte zorunludur (manifest doğrulaması).
            else if (package.Manifest.SupportsDryRun)
            {
                var timeout = TimeSpan.FromSeconds(Math.Min(package.Manifest.TimeoutSeconds, MaxPreviewSeconds));
                var preview = await SafeRunAsync(package, bound, dryRun: true, timeout, ct);
                run.PreviewOutput = SanitizedOutput(preview);
                if (!preview.Succeeded)
                {
                    run.Status = RunbookRunStatus.PreviewFailed;
                    run.FinishedAtUtc = Now;
                    run.Error = preview.TimedOut ? "Önizleme zaman aşımına uğradı." : $"Önizleme başarısız (çıkış kodu {preview.ExitCode}).";
                }
            }

            _db.RunbookRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunRequested)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = $"{version.RunbookId}@{version.Version}",
                Scope = run.TargetSummary,
                Success = run.Status != RunbookRunStatus.PreviewFailed,
                ErrorMessage = run.Error,
                After = new { run.Status, version.Risk, run.RequiredApprovals, run.Justification, run.ScheduleId },
                ActorUserName = actorType == null ? null : requester,
                ActorType = actorType,
            }, ct);

            return run;

        }

        /// <summary>
        /// Zamanlama/alarm tetiklemesiyle çalıştırma oluşturur (ADR-002 §5). Talep eden = zamanlamayı oluşturan kişi
        /// (dört göz: kendi zamanlamasının işini onaylayamaz). Parametreler zamanlama oluşturulurken kapsamla doğrulandı;
        /// burada imza/güven ve şema yeniden doğrulanır.
        /// </summary>
        public async Task<RunbookOperationResult> CreateScheduledRunAsync(RunbookSchedule schedule, int requiredApprovals, string trigger, CancellationToken ct = default)
        {
            var version = await _db.RunbookVersions.FirstOrDefaultAsync(v => v.Id == schedule.RunbookVersionId, ct);
            if (version == null)
                return RunbookOperationResult.Fail("Runbook sürümü bulunamadı.");

            var (package, error) = await _catalog.VerifyAsync(version, ct);
            if (package == null)
                return RunbookOperationResult.Fail(error ?? "Runbook doğrulanamadı.");

            var bound = RunbookParameterBinder.Bind(package.Manifest, ToInput(schedule.ParametersJson), OrganizationalScope.Global);
            if (!bound.IsValid)
                return new RunbookOperationResult(false, "Zamanlamanın parametreleri geçersiz.", null, bound.Errors);

            Agent? agent = null;
            if (package.Manifest.Executor == RunbookExecutors.Agent)
            {
                agent = schedule.TargetAgentId == null ? null : await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == schedule.TargetAgentId, ct);
                if (agent == null || !agent.IsEnabled)
                    return RunbookOperationResult.Fail("Hedef agent bulunamadı veya devre dışı.");
            }

            var run = await CreateRunAsync(version, package, bound, agent, schedule.CreatedBy,
                $"Zamanlama: {schedule.Name} ({trigger})", requiredApprovals, schedule.Id, "Schedule", ct);
            return new RunbookOperationResult(true, null, run);
        }

        /// <summary>Çalıştırma sonuçlandı mı (zamanlamanın üst üste binme kontrolü için).</summary>
        public async Task<bool> IsTerminalAsync(Guid runId, CancellationToken ct = default)
        {
            var status = await _db.RunbookRuns.Where(r => r.Id == runId).Select(r => r.Status).FirstOrDefaultAsync(ct);
            return status == null || RunbookRunStatus.Terminal.Contains(status);
        }

        // ------------------------------------------------------------------ Onay / Ret / İptal
        public async Task<RunbookOperationResult> ApproveAsync(ClaimsPrincipal user, Guid runId, string? comment, CancellationToken ct = default)
        {
            var approver = UserName(user);
            var run = await LoadAsync(runId, ct);
            if (run == null || approver == null)
                return RunbookOperationResult.Fail("Talep bulunamadı.");

            var check = await CheckDecisionAsync(user, approver, run, ct);
            if (check != null)
            {
                await AuditDecisionDeniedAsync(run, AuditActions.AutomationRunApproved, check, ct);
                return RunbookOperationResult.Fail(check);
            }

            AddApproval(run, new RunbookApproval
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Approver = approver,
                Decision = "approved",
                Comment = Truncate(comment?.Trim(), 500),
                DecidedAtUtc = Now,
            });

            if (run.Approvals.Count(a => a.Decision == "approved") >= run.RequiredApprovals)
            {
                run.Status = RunbookRunStatus.Approved;
                if (run.TargetAgentId != null)
                    run.ApprovalExpiresAtUtc = Now.AddHours(Math.Clamp(_options.ApprovalExpiryHours, 1, 24 * 14));
            }

            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunApproved)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = Describe(run),
                Scope = run.TargetSummary,
                After = new { run.Status, Comment = comment },
            }, ct);

            return new RunbookOperationResult(true, null, run);
        }

        public async Task<RunbookOperationResult> RejectAsync(ClaimsPrincipal user, Guid runId, string? comment, CancellationToken ct = default)
        {
            var approver = UserName(user);
            var run = await LoadAsync(runId, ct);
            if (run == null || approver == null)
                return RunbookOperationResult.Fail("Talep bulunamadı.");

            var check = await CheckDecisionAsync(user, approver, run, ct);
            if (check != null)
                return RunbookOperationResult.Fail(check);

            AddApproval(run, new RunbookApproval
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Approver = approver,
                Decision = "rejected",
                Comment = Truncate(comment?.Trim(), 500),
                DecidedAtUtc = Now,
            });
            run.Status = RunbookRunStatus.Rejected;
            run.FinishedAtUtc = Now;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunRejected)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = Describe(run),
                After = new { Comment = comment },
            }, ct);

            return new RunbookOperationResult(true, null, run);
        }

        /// <summary>Talep eden kişi veya automation.manage sahibi, çalışmaya başlamamış bir talebi iptal edebilir.</summary>
        public async Task<RunbookOperationResult> CancelAsync(ClaimsPrincipal user, Guid runId, CancellationToken ct = default)
        {
            var name = UserName(user);
            var run = await LoadAsync(runId, ct);
            if (run == null || name == null)
                return RunbookOperationResult.Fail("Talep bulunamadı.");

            var isOwner = string.Equals(run.RequestedBy, name, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !await _permissions.HasPermissionAsync(user, Permissions.AutomationManage, ct))
                return RunbookOperationResult.Fail("Yalnızca talep eden kişi veya otomasyon yöneticisi iptal edebilir.");

            if (run.Status is not (RunbookRunStatus.PendingApproval or RunbookRunStatus.Approved or RunbookRunStatus.PreviewPending))
                return RunbookOperationResult.Fail("Bu aşamadaki talep iptal edilemez.");

            var updated = await _db.RunbookRuns
                .Where(r => r.Id == run.Id && (r.Status == RunbookRunStatus.PendingApproval || r.Status == RunbookRunStatus.Approved || r.Status == RunbookRunStatus.PreviewPending))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RunbookRunStatus.Cancelled)
                    .SetProperty(r => r.FinishedAtUtc, Now), ct);
            if (updated == 0)
                return RunbookOperationResult.Fail("Talep bu arada çalışmaya başladı veya sonuçlandı.");

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunCancelled)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = Describe(run),
            }, ct);

            return new RunbookOperationResult(true, null, run);
        }

        /// <summary>Onay kuralları: bekleyen + süresi dolmamış talep, onaylayan ≠ talep eden, izin, hedef onaylayanın kapsamında.</summary>
        private async Task<string?> CheckDecisionAsync(ClaimsPrincipal user, string approver, RunbookRun run, CancellationToken ct)
        {
            if (run.Status != RunbookRunStatus.PendingApproval)
                return "Talep onay beklemiyor.";
            if (run.ApprovalExpiresAtUtc is { } expires && expires < Now)
                return "Talebin onay süresi dolmuş.";
            if (string.Equals(run.RequestedBy, approver, StringComparison.OrdinalIgnoreCase))
                return "Kendi talebinizi onaylayamazsınız (dört göz ilkesi).";
            if (run.Approvals.Any(a => string.Equals(a.Approver, approver, StringComparison.OrdinalIgnoreCase)))
                return "Bu talep için zaten karar verdiniz.";
            if (!await _permissions.HasPermissionAsync(user, Permissions.AutomationApprove, ct))
                return "Onay yetkiniz yok.";

            var version = run.RunbookVersion ?? await _db.RunbookVersions.FirstAsync(v => v.Id == run.RunbookVersionId, ct);
            var (package, error) = await _catalog.VerifyAsync(version, ct);
            if (package == null)
                return error ?? "Runbook doğrulanamadı.";

            var scope = await _scopes.ResolveAsync(user, ct);
            if (package.Manifest.Executor == RunbookExecutors.Agent)
            {
                var agent = run.TargetAgentId == null ? null : await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == run.TargetAgentId, ct);
                if (agent == null || !AgentInScope(user, scope, agent))
                    return "Hedef sunucu sizin yetki kapsamınızda değil.";
            }

            var rebound = RunbookParameterBinder.Bind(package.Manifest, ToInput(run.ParametersJson), scope);
            if (!rebound.IsValid)
                return "Talebin hedefi sizin yetki kapsamınızda değil: " + string.Join(" ", rebound.Errors);

            return null;
        }

        /// <summary>Onay kaydı DbSet üzerinden eklenir (önceden atanmış Guid anahtarı koleksiyon üzerinden eklenince EF güncelleme sanır).</summary>
        private void AddApproval(RunbookRun run, RunbookApproval approval)
        {
            _db.RunbookApprovals.Add(approval);
            if (!run.Approvals.Contains(approval))
                run.Approvals.Add(approval);
        }

        private async Task AuditDecisionDeniedAsync(RunbookRun run, string action, string reason, CancellationToken ct)
            => await _audit.RecordAsync(new AuditRecord(action)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = Describe(run),
                Success = false,
                ErrorMessage = reason,
            }, ct);

        // ------------------------------------------------------------------ Çalıştırma (arka plan)
        /// <summary>Kuyruktaki en eski onaylı talebi çalıştırır. Çalıştırılacak iş yoksa false.</summary>
        public async Task<bool> ExecuteNextAsync(CancellationToken ct = default)
        {
            var candidate = await _db.RunbookRuns
                .Where(r => r.Status == RunbookRunStatus.Approved && r.TargetAgentId == null)
                .OrderBy(r => r.RequestedAtUtc)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(ct);
            if (candidate == Guid.Empty)
                return false;

            // İyimser kilit: aynı işi birden fazla çalışan almasın.
            var claimed = await _db.RunbookRuns
                .Where(r => r.Id == candidate && r.Status == RunbookRunStatus.Approved)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RunbookRunStatus.Running)
                    .SetProperty(r => r.StartedAtUtc, Now), ct);
            if (claimed == 0)
                return true;

            var run = await LoadAsync(candidate, ct);
            if (run == null)
                return true;

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunStarted)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = Describe(run),
                Scope = run.TargetSummary,
                ActorUserName = "automation",
                ActorType = "System",
            }, ct);

            var (package, error) = await _catalog.VerifyAsync(run.RunbookVersion!, ct);
            RunbookScriptResult result;
            if (package == null)
            {
                result = new RunbookScriptResult(-1, string.Empty, error ?? "Runbook doğrulanamadı.", false);
            }
            else
            {
                // Parametreler talep ve onay sırasında kapsam kontrolünden geçti; burada yalnızca şema yeniden doğrulanır.
                var bound = RunbookParameterBinder.Bind(package.Manifest, ToInput(run.ParametersJson), OrganizationalScope.Global);
                result = bound.IsValid
                    ? await SafeRunAsync(package, bound, dryRun: false, TimeSpan.FromSeconds(package.Manifest.TimeoutSeconds), ct)
                    : new RunbookScriptResult(-1, string.Empty, "Saklanan parametreler geçersiz.", false);
            }

            run.Status = result.TimedOut ? RunbookRunStatus.TimedOut : result.Succeeded ? RunbookRunStatus.Succeeded : RunbookRunStatus.Failed;
            run.ExitCode = result.ExitCode;
            run.FinishedAtUtc = Now;
            run.Output = SanitizedOutput(result);
            if (result.Succeeded)
            {
                run.Error = null;
            }
            else
            {
                var stderr = Truncate(RunbookOutputSanitizer.Sanitize(result.StdErr, 4000), 2000);
                run.Error = !string.IsNullOrWhiteSpace(stderr) ? stderr
                    : result.TimedOut ? "Zaman aşımı." : $"Çıkış kodu {result.ExitCode}.";
            }
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunCompleted)
            {
                ResourceType = "RunbookRun",
                ResourceId = run.Id.ToString(),
                ResourceName = Describe(run),
                Scope = run.TargetSummary,
                Success = result.Succeeded,
                ErrorMessage = run.Error,
                ActorUserName = "automation",
                ActorType = "System",
                After = new { run.Status, run.ExitCode },
            }, ct);

            return true;
        }

        /// <summary>Onay süresi dolan talepleri Expired yapar.</summary>
        public async Task<int> ExpirePendingAsync(CancellationToken ct = default)
        {
            var now = Now;
            // Onay bekleyenler + agent'ın henüz almadığı işler (önizleme veya onaylı çalıştırma).
            var expired = await _db.RunbookRuns
                .Where(r => r.ApprovalExpiresAtUtc != null && r.ApprovalExpiresAtUtc < now &&
                            (r.Status == RunbookRunStatus.PendingApproval ||
                             (r.TargetAgentId != null && (r.Status == RunbookRunStatus.PreviewPending || r.Status == RunbookRunStatus.Approved))))
                .Select(r => new { r.Id, r.Status })
                .ToListAsync(ct);

            foreach (var item in expired)
            {
                var id = item.Id;
                var error = item.Status == RunbookRunStatus.PendingApproval ? null : "Agent işi süresi içinde almadı (sunucu kapalı veya otomasyon kapalı olabilir).";
                var updated = await _db.RunbookRuns
                    .Where(r => r.Id == id && r.Status == item.Status)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, RunbookRunStatus.Expired)
                        .SetProperty(r => r.FinishedAtUtc, now)
                        .SetProperty(r => r.Error, error), ct);
                if (updated == 0)
                    continue;

                await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunExpired)
                {
                    ResourceType = "RunbookRun",
                    ResourceId = id.ToString(),
                    ActorUserName = "automation",
                    ActorType = "System",
                }, ct);
            }

            return expired.Count;
        }

        /// <summary>
        /// Agent'a teslim edilmiş ama son tarihe kadar sonucu bildirilmemiş işler. Betik çalışmış olabilir;
        /// sonuç bilinmediği için yeniden çalıştırılmaz.
        /// </summary>
        public async Task<int> ExpireOverdueDispatchesAsync(CancellationToken ct = default)
        {
            var now = Now;
            var overdue = await _db.RunbookRuns
                .Where(r => r.DeadlineUtc != null && r.DeadlineUtc < now &&
                            (r.Status == RunbookRunStatus.PreviewRunning || r.Status == RunbookRunStatus.Dispatched))
                .Select(r => new { r.Id, r.Status })
                .ToListAsync(ct);

            foreach (var item in overdue)
            {
                var preview = item.Status == RunbookRunStatus.PreviewRunning;
                var updated = await _db.RunbookRuns
                    .Where(r => r.Id == item.Id && r.Status == item.Status)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, preview ? RunbookRunStatus.PreviewFailed : RunbookRunStatus.TimedOut)
                        .SetProperty(r => r.FinishedAtUtc, now)
                        .SetProperty(r => r.Error, preview
                            ? "Agent önizleme sonucunu süresi içinde bildirmedi."
                            : "Agent sonucu süresi içinde bildirmedi; iş çalışmış olabilir. Hedef sunucuyu kontrol edin."), ct);
                if (updated == 0)
                    continue;

                await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunCompleted)
                {
                    ResourceType = "RunbookRun",
                    ResourceId = item.Id.ToString(),
                    Success = false,
                    ErrorMessage = "Agent sonucu bildirmedi.",
                    ActorUserName = "automation",
                    ActorType = "System",
                }, ct);
            }

            return overdue.Count;
        }

        /// <summary>Uygulama yeniden başlatıldığında yarıda kalan işler: sonucu bilinmediği için Failed (yeniden çalıştırılmaz).</summary>
        public async Task<int> FailInterruptedAsync(CancellationToken ct = default)
            => await _db.RunbookRuns
                .Where(r => r.Status == RunbookRunStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RunbookRunStatus.Failed)
                    .SetProperty(r => r.FinishedAtUtc, Now)
                    .SetProperty(r => r.Error, "Uygulama yeniden başladığında çalışma yarıda kaldı; sonuç bilinmiyor. Hedefi kontrol edin."), ct);

        /// <summary>
        /// İmza anahtarı iptal edildiğinde (Faz 4): o anahtarla imzalı sürümlerin henüz çalışmaya başlamamış talepleri
        /// iptal edilir. Çalışmakta olan veya agent'a teslim edilmiş işler durdurulamaz; sonuçları normal şekilde kaydedilir.
        /// </summary>
        public async Task<int> CancelSignedByAsync(string fingerprint, string actor, CancellationToken ct = default)
        {
            var now = Now;
            var runs = await _db.RunbookRuns.AsNoTracking()
                .Where(r => r.RunbookVersion!.KeyFingerprint == fingerprint &&
                            (r.Status == RunbookRunStatus.PendingApproval || r.Status == RunbookRunStatus.Approved || r.Status == RunbookRunStatus.PreviewPending))
                .Select(r => new { r.Id, r.Status, r.RunbookVersion!.RunbookId, r.RunbookVersion.Version })
                .ToListAsync(ct);

            var cancelled = 0;
            foreach (var item in runs)
            {
                var id = item.Id;
                var updated = await _db.RunbookRuns
                    .Where(r => r.Id == id && r.Status == item.Status)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, RunbookRunStatus.Cancelled)
                        .SetProperty(r => r.FinishedAtUtc, now)
                        .SetProperty(r => r.Error, "Runbook'u imzalayan anahtar iptal edildi."), ct);
                if (updated == 0)
                    continue;

                cancelled++;
                await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunCancelled)
                {
                    ResourceType = "RunbookRun",
                    ResourceId = id.ToString(),
                    ResourceName = $"{item.RunbookId}@{item.Version}",
                    ActorUserName = actor,
                    After = new { Reason = "key-revoked", KeyFingerprint = fingerprint },
                }, ct);
            }

            return cancelled;
        }

        // ------------------------------------------------------------------ Agent çalıştırıcısı (Faz 2)
        private const int DispatchGraceSeconds = 300;

        /// <summary>
        /// Agent kapsamı: global kapsam → tüm agent'lar; aksi halde agent'ın birimi (Province) kullanıcının
        /// birim claim'lerinden (sp:province) biriyle eşleşmeli. Birimi atanmamış agent yalnızca global kapsamla hedeflenir.
        /// </summary>
        public static bool AgentInScope(ClaimsPrincipal user, OrganizationalScope scope, Agent agent)
        {
            if (scope.IsGlobal)
                return true;
            if (string.IsNullOrWhiteSpace(agent.Province))
                return false;
            return user.FindAll(ScopeClaimTypes.Province)
                .Any(c => string.Equals(c.Value?.Trim(), agent.Province.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Kullanıcının hedefleyebileceği etkin agent'lar.</summary>
        public async Task<IReadOnlyList<Agent>> ListTargetAgentsAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(user, ct);
            var agents = await _db.Agents.AsNoTracking().Where(a => a.IsEnabled).OrderBy(a => a.Hostname).ToListAsync(ct);
            return agents.Where(a => AgentInScope(user, scope, a)).ToList();
        }

        /// <summary>
        /// Agent'a atanmış sıradaki işi (önizleme veya onaylı çalıştırma) atomik olarak teslim eder.
        /// Paket teslimden hemen önce yeniden doğrulanır (anahtar iptal edildiyse iş başarısız olur).
        /// </summary>
        public async Task<AgentCommand?> ClaimNextForAgentAsync(Agent agent, CancellationToken ct = default)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var candidate = await _db.RunbookRuns.AsNoTracking()
                    .Include(r => r.RunbookVersion)
                    .Where(r => r.TargetAgentId == agent.Id &&
                                (r.Status == RunbookRunStatus.PreviewPending || r.Status == RunbookRunStatus.Approved))
                    .OrderBy(r => r.RequestedAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (candidate == null)
                    return null;

                var preview = candidate.Status == RunbookRunStatus.PreviewPending;
                var (package, error) = await _catalog.VerifyAsync(candidate.RunbookVersion!, ct);
                if (package == null || package.Manifest.Executor != RunbookExecutors.Agent)
                {
                    await _db.RunbookRuns
                        .Where(r => r.Id == candidate.Id && r.Status == candidate.Status)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.Status, preview ? RunbookRunStatus.PreviewFailed : RunbookRunStatus.Failed)
                            .SetProperty(r => r.FinishedAtUtc, Now)
                            .SetProperty(r => r.Error, error ?? "Runbook agent çalıştırıcısı için değil."), ct);
                    continue;
                }

                var timeout = preview ? Math.Min(package.Manifest.TimeoutSeconds, MaxPreviewSeconds) : package.Manifest.TimeoutSeconds;
                var now = Now;
                var deadline = now.AddSeconds(timeout + DispatchGraceSeconds);
                var claimQuery = _db.RunbookRuns.Where(r => r.Id == candidate.Id && r.Status == candidate.Status && r.TargetAgentId == agent.Id);
                var claimed = preview
                    ? await claimQuery.ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, RunbookRunStatus.PreviewRunning)
                        .SetProperty(r => r.DispatchedAtUtc, now)
                        .SetProperty(r => r.DeadlineUtc, deadline), ct)
                    : await claimQuery.ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, RunbookRunStatus.Dispatched)
                        .SetProperty(r => r.DispatchedAtUtc, now)
                        .SetProperty(r => r.DeadlineUtc, deadline)
                        .SetProperty(r => r.StartedAtUtc, now), ct);
                if (claimed == 0)
                    continue;

                if (!preview)
                {
                    await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunStarted)
                    {
                        ResourceType = "RunbookRun",
                        ResourceId = candidate.Id.ToString(),
                        ResourceName = Describe(candidate),
                        Scope = candidate.TargetSummary,
                        ActorUserName = agent.Hostname,
                        ActorType = "Agent",
                    }, ct);
                }

                var input = ToInput(candidate.ParametersJson);
                var stored = RunbookParameterCore.Bind(package.Manifest, input, null);
                return new AgentCommand
                {
                    RunId = candidate.Id,
                    Package = candidate.RunbookVersion!.PackageText,
                    Values = stored.Values.ToDictionary(kv => kv.Key, kv => kv.Value),
                    Switches = stored.Switches.ToList(),
                    DryRun = preview,
                    TimeoutSeconds = timeout,
                };
            }

            return null;
        }

        /// <summary>Agent'ın bildirdiği sonucu işler. Dönüş: 204 (kabul), 404 (iş bu agent'ın değil), 409 (beklenmeyen durum).</summary>
        public async Task<int> CompleteAgentCommandAsync(Agent agent, Guid runId, AgentCommandResult result, CancellationToken ct = default)
        {
            var run = await _db.RunbookRuns.AsNoTracking().Include(r => r.RunbookVersion).FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null || run.TargetAgentId != agent.Id)
                return 404;

            var expected = result.DryRun ? RunbookRunStatus.PreviewRunning : RunbookRunStatus.Dispatched;
            if (run.Status != expected)
                return 409;

            var scriptResult = new RunbookScriptResult(result.ExitCode, result.StdOut ?? string.Empty, result.StdErr ?? string.Empty, result.TimedOut);
            var output = SanitizedOutput(scriptResult);
            var ok = scriptResult.Succeeded && !result.RejectedByAgent;
            var now = Now;
            string? error = null;
            if (!ok)
            {
                var stderr = Truncate(RunbookOutputSanitizer.Sanitize(result.StdErr, 4000), 1800);
                error = result.RejectedByAgent ? "Agent işi reddetti: " + stderr
                    : !string.IsNullOrWhiteSpace(stderr) ? stderr
                    : result.TimedOut ? "Zaman aşımı." : $"Çıkış kodu {result.ExitCode}.";
            }

            int updated;
            if (result.DryRun)
            {
                var next = !ok ? RunbookRunStatus.PreviewFailed
                    : run.RequiredApprovals == 0 ? RunbookRunStatus.Approved : RunbookRunStatus.PendingApproval;
                var expires = now.AddHours(Math.Clamp(_options.ApprovalExpiryHours, 1, 24 * 14));
                updated = await _db.RunbookRuns
                    .Where(r => r.Id == runId && r.Status == expected)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, next)
                        .SetProperty(r => r.PreviewOutput, output)
                        .SetProperty(r => r.Error, error)
                        .SetProperty(r => r.DispatchedAtUtc, (DateTime?)null)
                        .SetProperty(r => r.DeadlineUtc, (DateTime?)null)
                        .SetProperty(r => r.ApprovalExpiresAtUtc, ok ? expires : null)
                        .SetProperty(r => r.FinishedAtUtc, ok ? null : now), ct);
            }
            else
            {
                var next = result.TimedOut ? RunbookRunStatus.TimedOut : ok ? RunbookRunStatus.Succeeded : RunbookRunStatus.Failed;
                updated = await _db.RunbookRuns
                    .Where(r => r.Id == runId && r.Status == expected)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, next)
                        .SetProperty(r => r.Output, output)
                        .SetProperty(r => r.Error, error)
                        .SetProperty(r => r.ExitCode, result.RejectedByAgent ? null : result.ExitCode)
                        .SetProperty(r => r.DeadlineUtc, (DateTime?)null)
                        .SetProperty(r => r.FinishedAtUtc, now), ct);
            }

            if (updated == 0)
                return 409;

            await _audit.RecordAsync(new AuditRecord(result.DryRun ? AuditActions.AutomationRunPreview : AuditActions.AutomationRunCompleted)
            {
                ResourceType = "RunbookRun",
                ResourceId = runId.ToString(),
                ResourceName = Describe(run),
                Scope = run.TargetSummary,
                Success = ok,
                ErrorMessage = error,
                ActorUserName = agent.Hostname,
                ActorType = "Agent",
                After = new { result.ExitCode, result.TimedOut, result.RejectedByAgent },
            }, ct);

            return 204;
        }

        // ------------------------------------------------------------------ Görünürlük
        /// <summary>
        /// Çalıştırma kayıtları hedef DN'leri içerir; başka birimlerin bilgisini sızdırmamak için:
        /// global kapsam → tümü; aksi halde kendi talepleri, karar verdikleri ve hedefi kapsamındaki talepler.
        /// </summary>
        public async Task<bool> CanViewAsync(ClaimsPrincipal user, RunbookRun run, CancellationToken ct = default)
        {
            var name = UserName(user);
            if (name == null)
                return false;

            var scope = await _scopes.ResolveAsync(user, ct);
            if (scope.IsGlobal)
                return true;
            if (string.Equals(run.RequestedBy, name, StringComparison.OrdinalIgnoreCase) ||
                run.Approvals.Any(a => string.Equals(a.Approver, name, StringComparison.OrdinalIgnoreCase)))
                return true;

            return await TargetsInScopeAsync(user, run, scope, new Dictionary<Guid, RunbookManifest?>(), ct);
        }

        public async Task<IReadOnlyList<RunbookRun>> ListVisibleAsync(ClaimsPrincipal user, string? status, int take = 200, CancellationToken ct = default)
        {
            var query = _db.RunbookRuns.AsNoTracking()
                .Include(r => r.RunbookVersion)
                .Include(r => r.Approvals)
                .OrderByDescending(r => r.RequestedAtUtc)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            var candidates = await query.Take(Math.Clamp(take, 1, 500) * 3).ToListAsync(ct);
            var scope = await _scopes.ResolveAsync(user, ct);
            if (scope.IsGlobal)
                return candidates.Take(take).ToList();

            var name = UserName(user);
            var manifests = new Dictionary<Guid, RunbookManifest?>();
            var visible = new List<RunbookRun>();
            foreach (var run in candidates)
            {
                if (visible.Count >= take)
                    break;
                if (string.Equals(run.RequestedBy, name, StringComparison.OrdinalIgnoreCase) ||
                    run.Approvals.Any(a => string.Equals(a.Approver, name, StringComparison.OrdinalIgnoreCase)) ||
                    await TargetsInScopeAsync(user, run, scope, manifests, ct))
                    visible.Add(run);
            }
            return visible;
        }

        /// <summary>Kullanıcının karar verebileceği bekleyen talepler (kendi talepleri hariç).</summary>
        public async Task<IReadOnlyList<RunbookRun>> ListAwaitingDecisionAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            var name = UserName(user);
            if (name == null || !await _permissions.HasPermissionAsync(user, Permissions.AutomationApprove, ct))
                return Array.Empty<RunbookRun>();

            var pending = await ListVisibleAsync(user, RunbookRunStatus.PendingApproval, 200, ct);
            return pending
                .Where(r => !string.Equals(r.RequestedBy, name, StringComparison.OrdinalIgnoreCase) &&
                            !r.Approvals.Any(a => string.Equals(a.Approver, name, StringComparison.OrdinalIgnoreCase)) &&
                            (r.ApprovalExpiresAtUtc == null || r.ApprovalExpiresAtUtc > Now))
                .ToList();
        }

        private async Task<bool> TargetsInScopeAsync(ClaimsPrincipal user, RunbookRun run, OrganizationalScope scope, Dictionary<Guid, RunbookManifest?> cache, CancellationToken ct)
        {
            if (run.TargetAgentId != null)
            {
                var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == run.TargetAgentId, ct);
                return agent != null && AgentInScope(user, scope, agent);
            }

            if (scope.IsEmpty)
                return false;

            if (!cache.TryGetValue(run.RunbookVersionId, out var manifest))
            {
                var version = run.RunbookVersion ?? await _db.RunbookVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == run.RunbookVersionId, ct);
                manifest = version == null ? null : (await _catalog.VerifyAsync(version, ct)).Package?.Manifest;
                cache[run.RunbookVersionId] = manifest;
            }

            if (manifest == null)
                return false;

            // Hedefsiz (ad-dn parametresi olmayan) runbook'ların kayıtları yalnızca ilgililere görünür.
            if (!manifest.Parameters.Any(p => p.Type == RunbookParameterTypes.AdDn && p.InScope))
                return false;

            return RunbookParameterBinder.Bind(manifest, ToInput(run.ParametersJson), scope).IsValid;
        }

        // ------------------------------------------------------------------ Yardımcılar
        public Task<RunbookRun?> LoadAsync(Guid runId, CancellationToken ct = default)
            => _db.RunbookRuns
                .Include(r => r.RunbookVersion)
                .Include(r => r.Approvals)
                .FirstOrDefaultAsync(r => r.Id == runId, ct);

        private async Task<RunbookScriptResult> SafeRunAsync(VerifiedRunbookPackage package, BoundRunbookParameters bound, bool dryRun, TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                return await _host.RunAsync(package, bound, dryRun, timeout, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Runbook çalıştırılamadı: {Runbook}", package.Manifest.Id);
                return new RunbookScriptResult(-1, string.Empty, "Runbook başlatılamadı: " + ex.Message, false);
            }
        }

        private sealed class StoredParameters
        {
            public Dictionary<string, string> Values { get; set; } = new();
            public List<string> Switches { get; set; } = new();
        }

        private static string SerializeParameters(BoundRunbookParameters bound)
            => JsonSerializer.Serialize(new StoredParameters { Values = bound.Values.ToDictionary(kv => kv.Key, kv => kv.Value), Switches = bound.Switches.ToList() });

        internal static IReadOnlyDictionary<string, string?> ToInput(string parametersJson)
        {
            var stored = JsonSerializer.Deserialize<StoredParameters>(parametersJson) ?? new StoredParameters();
            var input = stored.Values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value, StringComparer.Ordinal);
            foreach (var s in stored.Switches)
                input[s] = "true";
            return input;
        }

        public static IReadOnlyDictionary<string, string?> DescribeParameters(string parametersJson) => ToInput(parametersJson);

        /// <summary>stdout ve stderr AYRI maskelenir (JSON çıktının yapısal maskelemesi bozulmasın), sonra birleştirilir.</summary>
        private string SanitizedOutput(RunbookScriptResult r)
        {
            var max = Math.Max(1024, _options.MaxOutputBytes);
            var stdout = RunbookOutputSanitizer.Sanitize(r.StdOut, max);
            var stderr = RunbookOutputSanitizer.Sanitize(r.StdErr, max / 4);
            return stderr.Length == 0 ? stdout : stdout + (stdout.Length > 0 ? "\n" : string.Empty) + "[stderr]\n" + stderr;
        }

        private static string Describe(RunbookRun run)
            => run.RunbookVersion == null ? run.Id.ToString() : $"{run.RunbookVersion.RunbookId}@{run.RunbookVersion.Version}";

        private static string? UserName(ClaimsPrincipal user)
            => user.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name) ? user.Identity.Name : null;

        private static string? Truncate(string? value, int max)
            => value == null ? null : value.Length > max ? value[..max] : value;
    }
}

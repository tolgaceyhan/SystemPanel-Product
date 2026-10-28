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

            var required = RequiredApprovalsFor(package.Manifest.Risk);
            var run = new RunbookRun
            {
                Id = Guid.NewGuid(),
                RunbookVersionId = version.Id,
                Status = required == 0 ? RunbookRunStatus.Approved : RunbookRunStatus.PendingApproval,
                ParametersJson = SerializeParameters(bound),
                TargetSummary = RunbookParameterBinder.Summarize(package.Manifest, bound),
                Justification = Truncate(justification?.Trim(), 500),
                RequestedBy = requester,
                RequestedAtUtc = Now,
                RequiredApprovals = required,
                ApprovalExpiresAtUtc = required == 0 ? null : Now.AddHours(Math.Clamp(_options.ApprovalExpiryHours, 1, 24 * 14)),
                CorrelationId = System.Diagnostics.Activity.Current?.Id is { } aid ? Truncate(aid, 64) : null,
            };

            // Önizleme: değişiklik yapmadan planı gösterir. Yüksek riskte zorunludur (manifest doğrulaması).
            if (package.Manifest.SupportsDryRun)
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
                After = new { run.Status, version.Risk, run.RequiredApprovals, run.Justification },
            }, ct);

            return new RunbookOperationResult(true, null, run);
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
                run.Status = RunbookRunStatus.Approved;

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

            if (run.Status is not (RunbookRunStatus.PendingApproval or RunbookRunStatus.Approved))
                return RunbookOperationResult.Fail("Bu aşamadaki talep iptal edilemez.");

            var updated = await _db.RunbookRuns
                .Where(r => r.Id == run.Id && (r.Status == RunbookRunStatus.PendingApproval || r.Status == RunbookRunStatus.Approved))
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
                .Where(r => r.Status == RunbookRunStatus.Approved)
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
            var expired = await _db.RunbookRuns
                .Where(r => r.Status == RunbookRunStatus.PendingApproval && r.ApprovalExpiresAtUtc != null && r.ApprovalExpiresAtUtc < now)
                .Select(r => r.Id)
                .ToListAsync(ct);

            foreach (var id in expired)
            {
                var updated = await _db.RunbookRuns
                    .Where(r => r.Id == id && r.Status == RunbookRunStatus.PendingApproval)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunbookRunStatus.Expired).SetProperty(r => r.FinishedAtUtc, now), ct);
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

        /// <summary>Uygulama yeniden başlatıldığında yarıda kalan işler: sonucu bilinmediği için Failed (yeniden çalıştırılmaz).</summary>
        public async Task<int> FailInterruptedAsync(CancellationToken ct = default)
            => await _db.RunbookRuns
                .Where(r => r.Status == RunbookRunStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RunbookRunStatus.Failed)
                    .SetProperty(r => r.FinishedAtUtc, Now)
                    .SetProperty(r => r.Error, "Uygulama yeniden başladığında çalışma yarıda kaldı; sonuç bilinmiyor. Hedefi kontrol edin."), ct);

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

            return await TargetsInScopeAsync(run, scope, new Dictionary<Guid, RunbookManifest?>(), ct);
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
                    await TargetsInScopeAsync(run, scope, manifests, ct))
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

        private async Task<bool> TargetsInScopeAsync(RunbookRun run, OrganizationalScope scope, Dictionary<Guid, RunbookManifest?> cache, CancellationToken ct)
        {
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

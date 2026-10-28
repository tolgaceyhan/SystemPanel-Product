using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public sealed class ScheduleRequest
    {
        public string Name { get; set; } = string.Empty;
        public Guid VersionId { get; set; }
        public IReadOnlyDictionary<string, string?> Input { get; set; } = new Dictionary<string, string?>();
        public Guid? TargetAgentId { get; set; }
        public string TriggerType { get; set; } = RunbookTriggerTypes.Cron;
        public string? CronExpression { get; set; }
        public string? AlarmMetric { get; set; }
        public string? AlarmDrive { get; set; }
        public int MinIntervalMinutes { get; set; } = 60;
        public string? Justification { get; set; }
    }

    public sealed record ScheduleOperationResult(bool Success, string? Error, RunbookSchedule? Schedule = null, IReadOnlyList<string>? ValidationErrors = null)
    {
        public static ScheduleOperationResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Zamanlanmış ve alarm tetiklemeli runbook'lar (ADR-002 §5).
    /// <list type="bullet">
    /// <item>Oluşturma: parametreler ve hedef oluşturanın kapsamıyla doğrulanır; sürüm ve parametreler sabitlenir.</item>
    /// <item>Düşük riskli zamanlama hemen etkindir; orta/yüksek riskli zamanlama başka bir yetkilinin onayını bekler.</item>
    /// <item>Cron tetiklemesi: düşük ve orta risk otomatik (önceden onaylı parametre kümesi), yüksek risk her seferinde onay ister.</item>
    /// <item>Alarm tetiklemesi: yalnızca düşük riskli agent runbook'u otomatik; diğerleri yalnızca onay talebi oluşturur.</item>
    /// <item>Önceki çalıştırma sonuçlanmadan yeni tetikleme yapılmaz; iki tetikleme arasında en az <c>MinIntervalMinutes</c> geçer.</item>
    /// </list>
    /// </summary>
    public sealed class RunbookScheduleService
    {
        private static readonly Regex Drive = new("^[A-Za-z]:$", RegexOptions.Compiled);
        private static readonly string[] AlarmMetrics = { "cpu", "ram", "disk" };

        private readonly ApplicationDbContext _db;
        private readonly RunbookCatalog _catalog;
        private readonly RunbookRunService _runs;
        private readonly IOrganizationalScopeResolver _scopes;
        private readonly IPermissionService _permissions;
        private readonly IAuditService _audit;
        private readonly AutomationOptions _options;
        private readonly ILogger<RunbookScheduleService> _logger;
        private readonly TimeProvider _time;

        public RunbookScheduleService(
            ApplicationDbContext db,
            RunbookCatalog catalog,
            RunbookRunService runs,
            IOrganizationalScopeResolver scopes,
            IPermissionService permissions,
            IAuditService audit,
            IOptions<AutomationOptions> options,
            ILogger<RunbookScheduleService> logger,
            TimeProvider? time = null)
        {
            _db = db;
            _catalog = catalog;
            _runs = runs;
            _scopes = scopes;
            _permissions = permissions;
            _audit = audit;
            _options = options.Value;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        /// <summary>Tetiklendiğinde oluşacak çalıştırmanın gerektirdiği onay sayısı.</summary>
        public static int RequiredApprovalsOnFire(string triggerType, string risk, string executor) => triggerType switch
        {
            RunbookTriggerTypes.Alarm => risk == RunbookRisks.Low && executor == RunbookExecutors.Agent ? 0 : 1,
            _ => risk == RunbookRisks.High ? 1 : 0,
        };

        // ------------------------------------------------------------------ Oluşturma / karar
        public async Task<ScheduleOperationResult> CreateAsync(ClaimsPrincipal user, ScheduleRequest request, CancellationToken ct = default)
        {
            var creator = user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
            if (string.IsNullOrWhiteSpace(creator))
                return ScheduleOperationResult.Fail("Kullanıcı belirlenemedi.");
            if (!await _permissions.HasPermissionAsync(user, Permissions.AutomationManage, ct))
                return ScheduleOperationResult.Fail("Zamanlama oluşturma yetkiniz yok.");

            var errors = new List<string>();
            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length is < 1 or > 100)
                errors.Add("Ad 1-100 karakter olmalı.");
            if (string.IsNullOrWhiteSpace(request.Justification))
                errors.Add("Gerekçe gerekli.");

            var minInterval = Math.Max(1, _options.MinScheduleIntervalMinutes);
            if (request.MinIntervalMinutes < minInterval || request.MinIntervalMinutes > 7 * 24 * 60)
                errors.Add($"En kısa tetikleme aralığı {minInterval}-{7 * 24 * 60} dakika olmalı.");

            var version = await _db.RunbookVersions.FirstOrDefaultAsync(v => v.Id == request.VersionId, ct);
            if (version == null)
                return ScheduleOperationResult.Fail("Runbook bulunamadı.");
            var (package, verifyError) = await _catalog.VerifyAsync(version, ct);
            if (package == null)
                return ScheduleOperationResult.Fail(verifyError ?? "Runbook doğrulanamadı.");
            var manifest = package.Manifest;

            var scope = await _scopes.ResolveAsync(user, ct);
            var bound = RunbookParameterBinder.Bind(manifest, request.Input, scope);
            errors.AddRange(bound.Errors);

            Agent? agent = null;
            if (manifest.Executor == RunbookExecutors.Agent)
            {
                agent = request.TargetAgentId == null ? null : await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.TargetAgentId, ct);
                if (agent == null || !agent.IsEnabled)
                    errors.Add("Kayıtlı ve etkin bir hedef sunucu (agent) seçin.");
                else if (!RunbookRunService.AgentInScope(user, scope, agent))
                    errors.Add("Hedef sunucu yetki kapsamınızın dışında.");
            }

            string? cron = null, metric = null, drive = null;
            if (request.TriggerType == RunbookTriggerTypes.Cron)
            {
                if (!CronSchedule.TryParse(request.CronExpression, out var parsed, out var cronError))
                    errors.Add(cronError!);
                else if (parsed!.NextAfter(Now, _options.ResolveTimeZone()) == null)
                    errors.Add("Cron ifadesi hiçbir zaman tetiklenmiyor.");
                else
                    cron = parsed.Expression;
            }
            else if (request.TriggerType == RunbookTriggerTypes.Alarm)
            {
                if (manifest.Executor != RunbookExecutors.Agent)
                    errors.Add("Alarm tetikleyicisi yalnızca agent runbook'larıyla kullanılır (alarmı veren sunucuda çalışır).");
                metric = request.AlarmMetric?.Trim().ToLowerInvariant();
                if (metric == null || !AlarmMetrics.Contains(metric))
                    errors.Add("Alarm türü cpu, ram veya disk olmalı.");
                if (!string.IsNullOrWhiteSpace(request.AlarmDrive))
                {
                    if (metric != "disk" || !Drive.IsMatch(request.AlarmDrive.Trim()))
                        errors.Add("Sürücü yalnızca disk alarmında ve \"C:\" biçiminde verilir.");
                    else
                        drive = request.AlarmDrive.Trim().ToUpperInvariant();
                }
            }
            else
            {
                errors.Add("Tetikleyici türü geçersiz.");
            }

            if (errors.Count > 0)
                return new ScheduleOperationResult(false, "Zamanlama geçersiz.", null, errors);

            var schedule = new RunbookSchedule
            {
                Id = Guid.NewGuid(),
                Name = name,
                RunbookVersionId = version.Id,
                TargetAgentId = agent?.Id,
                ParametersJson = JsonSerializer.Serialize(new
                {
                    Values = bound.Values,
                    Switches = bound.Switches,
                }),
                TargetSummary = agent?.Hostname ?? RunbookParameterBinder.Summarize(manifest, bound),
                TriggerType = request.TriggerType,
                CronExpression = cron,
                AlarmMetric = metric,
                AlarmDrive = drive,
                MinIntervalMinutes = request.MinIntervalMinutes,
                Status = manifest.Risk == RunbookRisks.Low ? RunbookScheduleStatus.Active : RunbookScheduleStatus.PendingApproval,
                Justification = Truncate(request.Justification!.Trim(), 500),
                CreatedBy = creator!,
                CreatedAtUtc = Now,
            };
            schedule.NextFireAtUtc = ComputeNext(schedule);

            _db.RunbookSchedules.Add(schedule);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationScheduleCreate)
            {
                ResourceType = "RunbookSchedule",
                ResourceId = schedule.Id.ToString(),
                ResourceName = schedule.Name,
                Scope = schedule.TargetSummary,
                After = new { version.RunbookId, version.Version, version.Risk, schedule.TriggerType, schedule.CronExpression, schedule.AlarmMetric, schedule.AlarmDrive, schedule.MinIntervalMinutes, schedule.Status },
            }, ct);

            return new ScheduleOperationResult(true, null, schedule);
        }

        public Task<ScheduleOperationResult> ApproveAsync(ClaimsPrincipal user, Guid id, CancellationToken ct = default)
            => DecideAsync(user, id, approve: true, ct);

        public Task<ScheduleOperationResult> RejectAsync(ClaimsPrincipal user, Guid id, CancellationToken ct = default)
            => DecideAsync(user, id, approve: false, ct);

        private async Task<ScheduleOperationResult> DecideAsync(ClaimsPrincipal user, Guid id, bool approve, CancellationToken ct)
        {
            var name = user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
            var schedule = await _db.RunbookSchedules.Include(s => s.RunbookVersion).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (schedule == null || name == null)
                return ScheduleOperationResult.Fail("Zamanlama bulunamadı.");

            string? denied = null;
            if (schedule.Status != RunbookScheduleStatus.PendingApproval)
                denied = "Zamanlama onay beklemiyor.";
            else if (string.Equals(schedule.CreatedBy, name, StringComparison.OrdinalIgnoreCase))
                denied = "Kendi zamanlamanızı onaylayamazsınız (dört göz ilkesi).";
            else if (!await _permissions.HasPermissionAsync(user, Permissions.AutomationApprove, ct))
                denied = "Onay yetkiniz yok.";
            else if (!await TargetInScopeAsync(user, schedule, ct))
                denied = "Zamanlamanın hedefi sizin yetki kapsamınızda değil.";

            if (denied != null)
            {
                await _audit.RecordAsync(new AuditRecord(approve ? AuditActions.AutomationScheduleApprove : AuditActions.AutomationScheduleReject)
                {
                    ResourceType = "RunbookSchedule",
                    ResourceId = schedule.Id.ToString(),
                    ResourceName = schedule.Name,
                    Success = false,
                    ErrorMessage = denied,
                }, ct);
                return ScheduleOperationResult.Fail(denied);
            }

            schedule.Status = approve ? RunbookScheduleStatus.Active : RunbookScheduleStatus.Rejected;
            schedule.DecidedBy = name;
            schedule.DecidedAtUtc = Now;
            schedule.NextFireAtUtc = approve ? ComputeNext(schedule) : null;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(approve ? AuditActions.AutomationScheduleApprove : AuditActions.AutomationScheduleReject)
            {
                ResourceType = "RunbookSchedule",
                ResourceId = schedule.Id.ToString(),
                ResourceName = schedule.Name,
                Scope = schedule.TargetSummary,
            }, ct);

            return new ScheduleOperationResult(true, null, schedule);
        }

        /// <summary>Oluşturan kişi veya automation.manage sahibi devre dışı bırakabilir. Yeniden etkinleştirme yok: yeni zamanlama oluşturulur.</summary>
        public async Task<ScheduleOperationResult> DisableAsync(ClaimsPrincipal user, Guid id, CancellationToken ct = default)
        {
            var name = user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
            var schedule = await _db.RunbookSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (schedule == null || name == null)
                return ScheduleOperationResult.Fail("Zamanlama bulunamadı.");
            if (!string.Equals(schedule.CreatedBy, name, StringComparison.OrdinalIgnoreCase) &&
                !await _permissions.HasPermissionAsync(user, Permissions.AutomationManage, ct))
                return ScheduleOperationResult.Fail("Yalnızca oluşturan kişi veya otomasyon yöneticisi devre dışı bırakabilir.");
            if (schedule.Status is not (RunbookScheduleStatus.Active or RunbookScheduleStatus.PendingApproval))
                return ScheduleOperationResult.Fail("Zamanlama zaten etkin değil.");

            schedule.Status = RunbookScheduleStatus.Disabled;
            schedule.DisabledBy = name;
            schedule.DisabledAtUtc = Now;
            schedule.NextFireAtUtc = null;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationScheduleDisable)
            {
                ResourceType = "RunbookSchedule",
                ResourceId = schedule.Id.ToString(),
                ResourceName = schedule.Name,
            }, ct);

            return new ScheduleOperationResult(true, null, schedule);
        }

        // ------------------------------------------------------------------ Görünürlük
        public async Task<IReadOnlyList<RunbookSchedule>> ListVisibleAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            var all = await _db.RunbookSchedules.AsNoTracking().Include(s => s.RunbookVersion)
                .OrderBy(s => s.Status).ThenBy(s => s.Name).Take(500).ToListAsync(ct);
            var scope = await _scopes.ResolveAsync(user, ct);
            if (scope.IsGlobal)
                return all;

            var name = user.Identity?.Name;
            var visible = new List<RunbookSchedule>();
            foreach (var s in all)
            {
                if (string.Equals(s.CreatedBy, name, StringComparison.OrdinalIgnoreCase) || await TargetInScopeAsync(user, s, ct))
                    visible.Add(s);
            }
            return visible;
        }

        public async Task<RunbookSchedule?> FindVisibleAsync(ClaimsPrincipal user, Guid id, CancellationToken ct = default)
            => (await ListVisibleAsync(user, ct)).FirstOrDefault(s => s.Id == id);

        private async Task<bool> TargetInScopeAsync(ClaimsPrincipal user, RunbookSchedule schedule, CancellationToken ct)
        {
            var scope = await _scopes.ResolveAsync(user, ct);
            if (schedule.TargetAgentId != null)
            {
                var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == schedule.TargetAgentId, ct);
                if (agent == null || !RunbookRunService.AgentInScope(user, scope, agent))
                    return false;
            }

            var version = schedule.RunbookVersion ?? await _db.RunbookVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == schedule.RunbookVersionId, ct);
            if (version == null)
                return false;
            var (package, _) = await _catalog.VerifyAsync(version, ct);
            if (package == null)
                return false;

            if (schedule.TargetAgentId == null && !package.Manifest.Parameters.Any(p => p.Type == RunbookParameterTypes.AdDn && p.InScope))
                return scope.IsGlobal;

            return RunbookParameterBinder.Bind(package.Manifest, RunbookRunService.DescribeParameters(schedule.ParametersJson), scope).IsValid;
        }

        // ------------------------------------------------------------------ Tetikleme
        /// <summary>Zamanı gelmiş cron zamanlamalarını tetikler. Dönen değer: oluşturulan çalıştırma sayısı.</summary>
        public async Task<int> FireDueAsync(CancellationToken ct = default)
        {
            var now = Now;
            var due = await _db.RunbookSchedules
                .Include(s => s.RunbookVersion)
                .Where(s => s.Status == RunbookScheduleStatus.Active && s.TriggerType == RunbookTriggerTypes.Cron &&
                            s.NextFireAtUtc != null && s.NextFireAtUtc <= now)
                .ToListAsync(ct);

            var created = 0;
            foreach (var schedule in due)
            {
                schedule.NextFireAtUtc = ComputeNext(schedule);
                if (await FireAsync(schedule, $"cron {schedule.CronExpression}", ct))
                    created++;
            }
            return created;
        }

        /// <summary>Alarmı veren sunucunun agent'ını hedefleyen ve alarm türüne uyan zamanlamaları tetikler.</summary>
        public async Task<int> OnAlarmAsync(AutomationAlarm alarm, CancellationToken ct = default)
        {
            var host = alarm.Hostname.Trim().ToUpperInvariant();
            var agentIds = await _db.Agents.AsNoTracking()
                .Where(a => a.IsEnabled && a.Hostname == host)
                .Select(a => a.Id)
                .ToListAsync(ct);
            if (agentIds.Count == 0)
                return 0;

            var metric = alarm.Metric.ToLowerInvariant();
            var drive = alarm.Drive?.ToUpperInvariant();
            var schedules = await _db.RunbookSchedules
                .Include(s => s.RunbookVersion)
                .Where(s => s.Status == RunbookScheduleStatus.Active && s.TriggerType == RunbookTriggerTypes.Alarm &&
                            s.TargetAgentId != null && agentIds.Contains(s.TargetAgentId.Value) && s.AlarmMetric == metric)
                .ToListAsync(ct);

            var created = 0;
            foreach (var schedule in schedules.Where(s => s.AlarmDrive == null || s.AlarmDrive == drive))
            {
                if (await FireAsync(schedule, $"alarm {alarm.Metric.ToUpperInvariant()}{(drive == null ? "" : " " + drive)} %{alarm.Value}", ct))
                    created++;
            }
            return created;
        }

        private async Task<bool> FireAsync(RunbookSchedule schedule, string trigger, CancellationToken ct)
        {
            var now = Now;
            string? skip = null;
            if (schedule.LastFiredAtUtc is { } last && last.AddMinutes(schedule.MinIntervalMinutes) > now)
                skip = "En kısa tetikleme aralığı dolmadı.";
            else if (schedule.LastRunId is { } lastRun && !await _runs.IsTerminalAsync(lastRun, ct))
                skip = "Önceki çalıştırma henüz sonuçlanmadı.";

            if (skip != null)
            {
                schedule.LastMessage = $"{now:yyyy-MM-dd HH:mm} UTC atlandı ({trigger}): {skip}";
                await _db.SaveChangesAsync(ct);
                return false;
            }

            var version = schedule.RunbookVersion!;
            var required = RequiredApprovalsOnFire(schedule.TriggerType, version.Risk, version.Executor);
            var result = await _runs.CreateScheduledRunAsync(schedule, required, trigger, ct);

            schedule.LastFiredAtUtc = now;
            if (result.Success)
            {
                schedule.LastRunId = result.Run!.Id;
                schedule.LastMessage = $"{now:yyyy-MM-dd HH:mm} UTC tetiklendi ({trigger}).";
            }
            else
            {
                var detail = result.ValidationErrors is { Count: > 0 } ? " " + string.Join(" ", result.ValidationErrors) : string.Empty;
                schedule.LastMessage = Truncate($"{now:yyyy-MM-dd HH:mm} UTC çalıştırılamadı ({trigger}): {result.Error}{detail}", 500);
                _logger.LogWarning("Zamanlama tetiklenemedi: {Schedule}: {Error}", schedule.Name, result.Error);
            }
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationScheduleFired)
            {
                ResourceType = "RunbookSchedule",
                ResourceId = schedule.Id.ToString(),
                ResourceName = schedule.Name,
                Scope = schedule.TargetSummary,
                Success = result.Success,
                ErrorMessage = result.Success ? null : result.Error,
                ActorUserName = "automation",
                ActorType = "System",
                After = new { Trigger = trigger, RunId = result.Run?.Id, RequiredApprovals = required },
            }, ct);

            return result.Success;
        }

        private DateTime? ComputeNext(RunbookSchedule schedule)
        {
            if (schedule.Status != RunbookScheduleStatus.Active || schedule.TriggerType != RunbookTriggerTypes.Cron)
                return null;
            return CronSchedule.TryParse(schedule.CronExpression, out var cron, out _)
                ? cron!.NextAfter(Now, _options.ResolveTimeZone())
                : null;
        }

        private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
    }
}

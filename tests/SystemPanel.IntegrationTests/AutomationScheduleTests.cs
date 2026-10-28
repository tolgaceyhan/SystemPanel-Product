using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemPanel.Automation;
using SystemPanel.Contracts.Agents;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Security.Scope;
using SystemPanel.Services.Agents;

namespace SystemPanel.IntegrationTests;

/// <summary>ADR-002 Faz 3: zamanlama ve alarm tetikleyicileri.</summary>
public sealed class AutomationScheduleTests : IDisposable
{
    private const string Ankara = "OU=ANKARA,OU=IL,DC=corp,DC=example";

    private readonly FakeRunbookScriptHost _host = new();
    private readonly SystemPanelFactory _factory;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public AutomationScheduleTests()
    {
        _factory = new SystemPanelFactory(s =>
        {
            s.RemoveAll<IRunbookScriptHost>();
            s.AddSingleton<IRunbookScriptHost>(_host);
        });
        _ = _factory.Server;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _key.Dispose();
    }

    private static ClaimsPrincipal User(string name, string role = "AutoAdmin")
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role),
            new Claim(ScopeClaimTypes.OuRoot, Ankara),
            new Claim(ScopeClaimTypes.Province, "ANKARA"),
        }, "Test"));

    private static readonly ClaimsPrincipal Owner = User("owner");
    private static readonly ClaimsPrincipal Approver = User("approver");

    private async Task<T> Scoped<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private sealed record Setup(Guid ServerLow, Guid ServerMedium, Guid ServerHigh, Guid AgentLow, Guid AgentMedium, AgentCredentials AnkaraAgent);

    private async Task<Setup> SetUpAsync()
    {
        return await Scoped(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var p in new[] { Permissions.AutomationView, Permissions.AutomationRun, Permissions.AutomationApprove, Permissions.AutomationManage })
                db.RolePermissions.Add(new RolePermission { RoleName = "AutoAdmin", Permission = p });
            db.RolePermissions.Add(new RolePermission { RoleName = "AutoRunner", Permission = Permissions.AutomationRun });
            await db.SaveChangesAsync();
            sp.GetRequiredService<IRolePermissionStore>().Invalidate();
            Assert.True((await sp.GetRequiredService<RunbookTrustStore>().AddAsync("Test", _key.ExportSubjectPublicKeyInfoPem(), "t")).Ok);

            var catalog = sp.GetRequiredService<RunbookCatalog>();
            async Task<Guid> Import(string id, string risk, string executor)
            {
                var agent = executor == RunbookExecutors.Agent;
                var m = new RunbookManifest
                {
                    Id = id, Version = "1.0.0", Title = id, Risk = risk, Executor = executor,
                    TargetType = agent ? RunbookTargetTypes.Server : RunbookTargetTypes.AdOu,
                    SupportsDryRun = risk == RunbookRisks.High,
                    Parameters = agent
                        ? new List<RunbookParameter> { new() { Name = "Days", Type = RunbookParameterTypes.Int, Min = 1, Max = 365 } }
                        : new List<RunbookParameter> { new() { Name = "SearchBase", Type = RunbookParameterTypes.AdDn } },
                };
                var r = await catalog.ImportAsync(RunbookPackageCodec.Sign(m, agent ? "param($Days)\n'ok'" : "param($SearchBase,[switch]$DryRun)\n'ok'", _key), "t");
                Assert.True(r.Success, r.Error);
                return r.Version!.Id;
            }

            var agentCreds = await sp.GetRequiredService<AgentRegistry>().RegisterAsync("SRV-ANK-01", "ANKARA", null, "t");
            return new Setup(
                await Import("test.server.low", RunbookRisks.Low, RunbookExecutors.Server),
                await Import("test.server.medium", RunbookRisks.Medium, RunbookExecutors.Server),
                await Import("test.server.high", RunbookRisks.High, RunbookExecutors.Server),
                await Import("test.agent.low", RunbookRisks.Low, RunbookExecutors.Agent),
                await Import("test.agent.medium", RunbookRisks.Medium, RunbookExecutors.Agent),
                agentCreds);
        });
    }

    private Task<ScheduleOperationResult> CreateCronAsync(Guid versionId, string cron = "0 3 * * *", ClaimsPrincipal? user = null, int interval = 60)
        => Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().CreateAsync(user ?? Owner, new ScheduleRequest
        {
            Name = "Gece raporu",
            VersionId = versionId,
            Input = new Dictionary<string, string?> { ["SearchBase"] = "OU=Bilgisayarlar," + Ankara },
            TriggerType = RunbookTriggerTypes.Cron,
            CronExpression = cron,
            MinIntervalMinutes = interval,
            Justification = "test",
        }));

    private Task<ScheduleOperationResult> CreateAlarmAsync(Guid versionId, Guid agentId, string metric = "disk", string? drive = "C:")
        => Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().CreateAsync(Owner, new ScheduleRequest
        {
            Name = "Disk dolunca",
            VersionId = versionId,
            Input = new Dictionary<string, string?> { ["Days"] = "30" },
            TargetAgentId = agentId,
            TriggerType = RunbookTriggerTypes.Alarm,
            AlarmMetric = metric,
            AlarmDrive = drive,
            MinIntervalMinutes = 60,
            Justification = "test",
        }));

    private Task MakeDueAsync(Guid scheduleId) => Scoped(async sp =>
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var s = await db.RunbookSchedules.SingleAsync(x => x.Id == scheduleId);
        s.NextFireAtUtc = DateTime.UtcNow.AddMinutes(-1);
        return await db.SaveChangesAsync();
    });

    private Task<int> FireDueAsync() => Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().FireDueAsync());

    private Task<List<RunbookRun>> RunsOfAsync(Guid scheduleId)
        => Scoped(sp => sp.GetRequiredService<ApplicationDbContext>().RunbookRuns.AsNoTracking().Where(r => r.ScheduleId == scheduleId).ToListAsync());

    private Task<RunbookSchedule> LoadScheduleAsync(Guid id)
        => Scoped(sp => sp.GetRequiredService<ApplicationDbContext>().RunbookSchedules.AsNoTracking().SingleAsync(s => s.Id == id));

    // ------------------------------------------------------------------ cron
    [Fact]
    public async Task LowRiskCron_IsActiveImmediately_AndFiresAutomatically()
    {
        var s = await SetUpAsync();
        var created = await CreateCronAsync(s.ServerLow);
        Assert.True(created.Success, created.Error + string.Join(";", created.ValidationErrors ?? Array.Empty<string>()));
        Assert.Equal(RunbookScheduleStatus.Active, created.Schedule!.Status);
        Assert.NotNull(created.Schedule.NextFireAtUtc);

        Assert.Equal(0, await FireDueAsync()); // henüz zamanı gelmedi
        await MakeDueAsync(created.Schedule.Id);
        Assert.Equal(1, await FireDueAsync());

        var run = Assert.Single(await RunsOfAsync(created.Schedule.Id));
        Assert.Equal(RunbookRunStatus.Approved, run.Status);
        Assert.Equal("owner", run.RequestedBy);
        Assert.Contains("Zamanlama", run.Justification);
        Assert.True((await LoadScheduleAsync(created.Schedule.Id)).NextFireAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Cron_DoesNotOverlap_AndRespectsMinInterval()
    {
        var s = await SetUpAsync();
        var created = await CreateCronAsync(s.ServerLow, "*/5 * * * *", interval: 5);
        await MakeDueAsync(created.Schedule!.Id);
        Assert.Equal(1, await FireDueAsync());

        // Önceki çalıştırma bitmedi → atlanır
        await MakeDueAsync(created.Schedule.Id);
        Assert.Equal(0, await FireDueAsync());
        var schedule = await LoadScheduleAsync(created.Schedule.Id);
        Assert.Contains("atlandı", schedule.LastMessage);
        Assert.Single(await RunsOfAsync(created.Schedule.Id));
    }

    [Fact]
    public async Task HighRiskCron_NeedsScheduleApproval_AndEachRunNeedsApproval()
    {
        var s = await SetUpAsync();
        var created = await CreateCronAsync(s.ServerHigh);
        Assert.Equal(RunbookScheduleStatus.PendingApproval, created.Schedule!.Status);
        Assert.Null(created.Schedule.NextFireAtUtc);

        var self = await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().ApproveAsync(Owner, created.Schedule.Id));
        Assert.False(self.Success);
        Assert.Contains("dört göz", self.Error);

        Assert.True((await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().ApproveAsync(Approver, created.Schedule.Id))).Success);
        await MakeDueAsync(created.Schedule.Id);
        Assert.Equal(1, await FireDueAsync());

        var run = Assert.Single(await RunsOfAsync(created.Schedule.Id));
        Assert.Equal(RunbookRunStatus.PendingApproval, run.Status); // yüksek risk: her çalıştırma onaylanır
        Assert.True(_host.Calls.Single().DryRun);                     // önizleme alındı

        // Zamanlamayı oluşturan kişi bu çalıştırmayı da onaylayamaz
        Assert.False((await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ApproveAsync(Owner, run.Id, null))).Success);
    }

    [Fact]
    public async Task MediumRiskCron_AfterApproval_RunsAutomatically()
    {
        var s = await SetUpAsync();
        var created = await CreateCronAsync(s.ServerMedium);
        Assert.Equal(RunbookScheduleStatus.PendingApproval, created.Schedule!.Status);
        Assert.True((await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().ApproveAsync(Approver, created.Schedule.Id))).Success);

        await MakeDueAsync(created.Schedule.Id);
        await FireDueAsync();

        Assert.Equal(RunbookRunStatus.Approved, Assert.Single(await RunsOfAsync(created.Schedule.Id)).Status);
    }

    [Fact]
    public async Task PendingOrDisabledSchedule_DoesNotFire()
    {
        var s = await SetUpAsync();
        var pending = await CreateCronAsync(s.ServerHigh);
        var low = await CreateCronAsync(s.ServerLow);
        Assert.True((await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().DisableAsync(Owner, low.Schedule!.Id))).Success);

        await MakeDueAsync(pending.Schedule!.Id);
        await MakeDueAsync(low.Schedule!.Id);
        Assert.Equal(0, await FireDueAsync());
    }

    [Fact]
    public async Task RevokedKey_FiringFails_WithMessage()
    {
        var s = await SetUpAsync();
        var created = await CreateCronAsync(s.ServerLow);
        await Scoped(async sp =>
        {
            var store = sp.GetRequiredService<RunbookTrustStore>();
            return await store.RevokeAsync((await store.ListAsync()).Single(k => !k.IsBuiltIn).Id!.Value, "t");
        });

        await MakeDueAsync(created.Schedule!.Id);
        Assert.Equal(0, await FireDueAsync());
        Assert.Contains("çalıştırılamadı", (await LoadScheduleAsync(created.Schedule.Id)).LastMessage);
    }

    [Theory]
    [InlineData("bad cron", 60)]
    [InlineData("0 3 * * *", 1)]
    [InlineData("0 0 30 2 *", 60)]
    public async Task InvalidSchedules_AreRejected(string cron, int interval)
    {
        var s = await SetUpAsync();
        Assert.False((await CreateCronAsync(s.ServerLow, cron, interval: interval)).Success);
    }

    [Fact]
    public async Task CreatingRequiresManagePermission()
    {
        var s = await SetUpAsync();
        var result = await CreateCronAsync(s.ServerLow, user: User("runner", "AutoRunner"));
        Assert.False(result.Success);
    }

    // ------------------------------------------------------------------ alarm
    [Fact]
    public async Task LowRiskAgentAlarm_RunsAutomatically_OnMatchingAlarmOnly()
    {
        var s = await SetUpAsync();
        var created = await CreateAlarmAsync(s.AgentLow, s.AnkaraAgent.AgentId);
        Assert.True(created.Success, string.Join(";", created.ValidationErrors ?? Array.Empty<string>()));
        Assert.Equal(RunbookScheduleStatus.Active, created.Schedule!.Status);

        var service = (Func<AutomationAlarm, Task<int>>)(a => Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().OnAlarmAsync(a)));
        Assert.Equal(0, await service(new AutomationAlarm("SRV-ANK-01", "disk", "D:", 95, DateTime.UtcNow)));   // başka sürücü
        Assert.Equal(0, await service(new AutomationAlarm("SRV-ANK-01", "cpu", null, 99, DateTime.UtcNow)));    // başka metrik
        Assert.Equal(0, await service(new AutomationAlarm("SRV-OTHER", "disk", "C:", 95, DateTime.UtcNow)));    // başka sunucu
        Assert.Equal(1, await service(new AutomationAlarm("srv-ank-01", "disk", "C:", 95, DateTime.UtcNow)));

        var run = Assert.Single(await RunsOfAsync(created.Schedule.Id));
        Assert.Equal(RunbookRunStatus.Approved, run.Status);
        Assert.Equal(s.AnkaraAgent.AgentId, run.TargetAgentId);

        // En kısa aralık dolmadan yeniden tetiklenmez
        Assert.Equal(0, await service(new AutomationAlarm("SRV-ANK-01", "disk", "C:", 97, DateTime.UtcNow)));
    }

    [Fact]
    public async Task MediumRiskAlarm_OnlyCreatesApprovalRequest()
    {
        var s = await SetUpAsync();
        var created = await CreateAlarmAsync(s.AgentMedium, s.AnkaraAgent.AgentId, metric: "cpu", drive: null);
        Assert.True((await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().ApproveAsync(Approver, created.Schedule!.Id))).Success);

        await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().OnAlarmAsync(new AutomationAlarm("SRV-ANK-01", "cpu", null, 99, DateTime.UtcNow)));

        var run = Assert.Single(await RunsOfAsync(created.Schedule!.Id));
        Assert.Equal(RunbookRunStatus.PendingApproval, run.Status);
        Assert.Equal(1, run.RequiredApprovals);
    }

    [Fact]
    public async Task AlarmTrigger_RequiresAgentRunbook()
    {
        var s = await SetUpAsync();
        var result = await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().CreateAsync(Owner, new ScheduleRequest
        {
            Name = "x", VersionId = s.ServerLow, TriggerType = RunbookTriggerTypes.Alarm, AlarmMetric = "cpu",
            Input = new Dictionary<string, string?> { ["SearchBase"] = "OU=B," + Ankara }, Justification = "t",
        }));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MetricsIngestion_PublishesAlarmsToAutomation()
    {
        var s = await SetUpAsync();
        var client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(AgentApi.AgentIdHeader, s.AnkaraAgent.AgentId.ToString());
        client.DefaultRequestHeaders.Add(AgentApi.AgentKeyHeader, s.AnkaraAgent.Key);

        var response = await client.PostAsJsonAsync("/" + AgentApi.MetricsPath, new AgentMetricsRequest
        {
            CollectedAtUtc = DateTime.UtcNow,
            Cpu = new CpuMetric { UsagePercent = 99 },
            Disks = { new DiskMetric { Name = "C:", UsagePercent = 97 } },
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var queue = _factory.Services.GetRequiredService<AutomationAlarmQueue>();
        var alarms = new List<AutomationAlarm>();
        while (queue.TryRead(out var a)) alarms.Add(a!);

        Assert.Contains(alarms, a => a.Metric == "cpu" && a.Hostname == "SRV-ANK-01");
        Assert.Contains(alarms, a => a.Metric == "disk" && a.Drive == "C:");
    }
}

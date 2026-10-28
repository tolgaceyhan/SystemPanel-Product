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

/// <summary>ADR-002 Faz 2: agent çalıştırıcılı runbook'ların teslimi ve sonuç bildirimi (HTTP, gerçek agent kimliği).</summary>
public sealed class AutomationAgentDispatchTests : IDisposable
{
    private readonly FakeRunbookScriptHost _host = new();
    private readonly SystemPanelFactory _factory;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public AutomationAgentDispatchTests()
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

    private static ClaimsPrincipal User(string name, params string[] provinces)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.Role, "AutoOps") };
        claims.AddRange(provinces.Select(p => new Claim(ScopeClaimTypes.Province, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static readonly ClaimsPrincipal Ops1 = User("ops1", "ANKARA");
    private static readonly ClaimsPrincipal Ops2 = User("ops2", "Ankara");
    private static readonly ClaimsPrincipal IzmirOps = User("ops3", "IZMIR");

    private async Task<T> Scoped<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private sealed record Setup(Guid VersionId, AgentCredentials Ankara, AgentCredentials Izmir);

    private async Task<Setup> SetUpAsync(string risk = RunbookRisks.Medium, bool dryRun = true)
    {
        return await Scoped(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var p in new[] { Permissions.AutomationView, Permissions.AutomationRun, Permissions.AutomationApprove })
                db.RolePermissions.Add(new RolePermission { RoleName = "AutoOps", Permission = p });
            await db.SaveChangesAsync();
            sp.GetRequiredService<IRolePermissionStore>().Invalidate();

            Assert.True((await sp.GetRequiredService<RunbookTrustStore>().AddAsync("Test", _key.ExportSubjectPublicKeyInfoPem(), "t")).Ok);

            var manifest = new RunbookManifest
            {
                Id = "server.service.restart",
                Version = "1.0.0",
                Title = "Servis yeniden başlat",
                Executor = RunbookExecutors.Agent,
                TargetType = RunbookTargetTypes.Server,
                Risk = risk,
                SupportsDryRun = dryRun,
                Parameters = { new RunbookParameter { Name = "ServiceName", Type = RunbookParameterTypes.String, Pattern = "[A-Za-z0-9_.-]{1,80}" } },
            };
            var imported = await sp.GetRequiredService<RunbookCatalog>().ImportAsync(
                RunbookPackageCodec.Sign(manifest, "param([string]$ServiceName,[switch]$DryRun)\n'ok'", _key), "t");
            Assert.True(imported.Success, imported.Error);

            var registry = sp.GetRequiredService<AgentRegistry>();
            var ankara = await registry.RegisterAsync("SRV-ANK-01", "ANKARA", null, "t");
            var izmir = await registry.RegisterAsync("SRV-IZM-01", "IZMIR", null, "t");
            return new Setup(imported.Version!.Id, ankara, izmir);
        });
    }

    private Task<RunbookOperationResult> RequestAsync(ClaimsPrincipal user, Setup s, Guid agentId, string service = "Spooler")
        => Scoped(sp => sp.GetRequiredService<RunbookRunService>().RequestAsync(user, s.VersionId,
            new Dictionary<string, string?> { ["ServiceName"] = service }, "iş kaydı 42", agentId));

    private Task<RunbookRun> LoadAsync(Guid id)
        => Scoped(async sp => (await sp.GetRequiredService<RunbookRunService>().LoadAsync(id))!);

    private HttpClient AgentClient(AgentCredentials c)
    {
        var client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(AgentApi.AgentIdHeader, c.AgentId.ToString());
        client.DefaultRequestHeaders.Add(AgentApi.AgentKeyHeader, c.Key);
        return client;
    }

    private static async Task<AgentCommand?> NextAsync(HttpClient agent)
    {
        var response = await agent.PostAsync("/" + AgentApi.NextCommandPath, null);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AgentCommand>();
    }

    private static Task<HttpResponseMessage> ReportAsync(HttpClient agent, Guid runId, AgentCommandResult result)
        => agent.PostAsJsonAsync("/" + string.Format(AgentApi.CommandResultPathFormat, runId), result);

    [Fact]
    public async Task FullFlow_PreviewOnAgent_FourEyes_ExecuteOnAgent()
    {
        var s = await SetUpAsync();
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);
        Assert.True(request.Success, string.Join(";", request.ValidationErrors ?? Array.Empty<string>()) + request.Error);
        Assert.Equal(RunbookRunStatus.PreviewPending, request.Run!.Status);
        Assert.Empty(_host.Calls); // sunucu agent işini kendisi çalıştırmaz

        var agent = AgentClient(s.Ankara);

        // 1) Önizleme agent'ta
        var preview = await NextAsync(agent);
        Assert.NotNull(preview);
        Assert.True(preview!.DryRun);
        Assert.Equal("Spooler", preview.Values["ServiceName"]);
        Assert.True(RunbookPackageCodec.TryVerify(preview.Package, _key, out var pkg, out _));
        Assert.Equal("server.service.restart", pkg!.Manifest.Id);
        Assert.Null(await NextAsync(agent)); // aynı iş iki kez teslim edilmez

        Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(agent, request.Run.Id,
            new AgentCommandResult { DryRun = true, ExitCode = 0, StdOut = "{\"plan\":\"restart Spooler\"}" })).StatusCode);
        var afterPreview = await LoadAsync(request.Run.Id);
        Assert.Equal(RunbookRunStatus.PendingApproval, afterPreview.Status);
        Assert.Contains("restart Spooler", afterPreview.PreviewOutput);

        // 2) Onay olmadan agent'a iş verilmez
        Assert.Null(await NextAsync(agent));
        Assert.False((await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ApproveAsync(Ops1, request.Run.Id, null))).Success);
        Assert.True((await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ApproveAsync(Ops2, request.Run.Id, null))).Success);

        // 3) Sunucu çalıştırıcısı agent işini almaz
        Assert.False(await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ExecuteNextAsync()));

        // 4) Çalıştırma agent'ta
        var exec = await NextAsync(agent);
        Assert.NotNull(exec);
        Assert.False(exec!.DryRun);
        Assert.Equal(RunbookRunStatus.Dispatched, (await LoadAsync(request.Run.Id)).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(agent, request.Run.Id,
            new AgentCommandResult { DryRun = false, ExitCode = 0, StdOut = "{\"restarted\":true,\"password\":\"x\"}" })).StatusCode);
        var done = await LoadAsync(request.Run.Id);
        Assert.Equal(RunbookRunStatus.Succeeded, done.Status);
        Assert.DoesNotContain("\"x\"", done.Output);

        // Tekrar bildirim kabul edilmez
        Assert.Equal(HttpStatusCode.Conflict, (await ReportAsync(agent, request.Run.Id, new AgentCommandResult { ExitCode = 0 })).StatusCode);

        var actions = await Scoped(sp => sp.GetRequiredService<ApplicationDbContext>().AuditEvents
            .Where(e => e.ResourceId == request.Run.Id.ToString()).Select(e => e.Action).ToListAsync());
        Assert.Contains("automation.run.preview", actions);
        Assert.Contains("automation.run.started", actions);
        Assert.Contains("automation.run.completed", actions);
    }

    [Fact]
    public async Task TargetAgentOutsideScope_IsRejected_AndNotListed()
    {
        var s = await SetUpAsync();

        var request = await RequestAsync(Ops1, s, s.Izmir.AgentId);
        Assert.False(request.Success);

        var targets = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ListTargetAgentsAsync(Ops1));
        Assert.Equal(new[] { "SRV-ANK-01" }, targets.Select(a => a.Hostname));
    }

    [Fact]
    public async Task AgentRunbook_RequiresTarget()
    {
        var s = await SetUpAsync();
        var request = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().RequestAsync(Ops1, s.VersionId,
            new Dictionary<string, string?> { ["ServiceName"] = "Spooler" }, "x"));
        Assert.False(request.Success);
    }

    [Fact]
    public async Task ApproverOutsideAgentScope_CannotApprove()
    {
        var s = await SetUpAsync(dryRun: false);
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);
        Assert.Equal(RunbookRunStatus.PendingApproval, request.Run!.Status);

        var result = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ApproveAsync(IzmirOps, request.Run.Id, null));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task OtherAgent_CannotFetchOrReport()
    {
        var s = await SetUpAsync(risk: RunbookRisks.Low, dryRun: false);
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);
        Assert.Equal(RunbookRunStatus.Approved, request.Run!.Status);

        var izmirAgent = AgentClient(s.Izmir);
        Assert.Null(await NextAsync(izmirAgent));

        var ankaraAgent = AgentClient(s.Ankara);
        Assert.NotNull(await NextAsync(ankaraAgent));
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(izmirAgent, request.Run.Id, new AgentCommandResult { ExitCode = 0 })).StatusCode);
    }

    [Fact]
    public async Task RequestForm_ListsTargetAgents()
    {
        var s = await SetUpAsync();
        var html = WebUtility.HtmlDecode(await (await _factory.CreateUserClient("Admin").GetAsync($"/automation/runbooks/{s.VersionId}/request")).Content.ReadAsStringAsync());

        Assert.Contains("Hedef sunucu", html);
        Assert.Contains("SRV-ANK-01", html);
        Assert.Contains("SRV-IZM-01", html); // Admin: global kapsam
    }

    [Fact]
    public async Task UnauthenticatedAgentCall_Is401()
    {
        var response = await _factory.CreateAnonymousClient().PostAsync("/" + AgentApi.NextCommandPath, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentRejection_FailsPreview()
    {
        var s = await SetUpAsync();
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);
        var agent = AgentClient(s.Ankara);
        await NextAsync(agent);

        await ReportAsync(agent, request.Run!.Id, new AgentCommandResult { DryRun = true, RejectedByAgent = true, ExitCode = -1, StdErr = "Runbook izin listesinde değil." });

        var run = await LoadAsync(request.Run.Id);
        Assert.Equal(RunbookRunStatus.PreviewFailed, run.Status);
        Assert.Contains("izin listesinde", run.Error);
    }

    [Fact]
    public async Task OverdueDispatch_TimesOut_AndIsNotRerun()
    {
        var s = await SetUpAsync(risk: RunbookRisks.Low, dryRun: false);
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);
        var agent = AgentClient(s.Ankara);
        Assert.NotNull(await NextAsync(agent));

        await Scoped(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var run = await db.RunbookRuns.SingleAsync(r => r.Id == request.Run!.Id);
            run.DeadlineUtc = DateTime.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });

        Assert.Equal(1, await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ExpireOverdueDispatchesAsync()));
        Assert.Equal(RunbookRunStatus.TimedOut, (await LoadAsync(request.Run!.Id)).Status);
        Assert.Null(await NextAsync(agent));
        Assert.Equal(HttpStatusCode.Conflict, (await ReportAsync(agent, request.Run.Id, new AgentCommandResult { ExitCode = 0 })).StatusCode);
    }

    [Fact]
    public async Task NotPickedUp_Expires()
    {
        var s = await SetUpAsync(risk: RunbookRisks.Low, dryRun: false);
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);

        await Scoped(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var run = await db.RunbookRuns.SingleAsync(r => r.Id == request.Run!.Id);
            run.ApprovalExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });

        await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ExpirePendingAsync());
        var expired = await LoadAsync(request.Run!.Id);
        Assert.Equal(RunbookRunStatus.Expired, expired.Status);
        Assert.Contains("Agent", expired.Error);
    }

    [Fact]
    public async Task RevokedKey_AgentGetsNothing_RunFails()
    {
        var s = await SetUpAsync(risk: RunbookRisks.Low, dryRun: false);
        var request = await RequestAsync(Ops1, s, s.Ankara.AgentId);

        await Scoped(async sp =>
        {
            var store = sp.GetRequiredService<RunbookTrustStore>();
            return await store.RevokeAsync((await store.ListAsync()).Single(k => !k.IsBuiltIn).Id!.Value, "t");
        });

        Assert.Null(await NextAsync(AgentClient(s.Ankara)));
        Assert.Equal(RunbookRunStatus.Failed, (await LoadAsync(request.Run!.Id)).Status);
    }
}

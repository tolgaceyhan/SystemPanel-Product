using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemPanel.Automation;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Security.Scope;

namespace SystemPanel.IntegrationTests;

/// <summary>Testlerde PowerShell yerine kullanılan çalıştırıcı: çağrıları kaydeder, sonucu testler belirler.</summary>
public sealed class FakeRunbookScriptHost : IRunbookScriptHost
{
    public ConcurrentQueue<(string RunbookId, BoundRunbookParameters Parameters, bool DryRun)> Calls { get; } = new();

    public Func<bool, RunbookScriptResult> Result { get; set; } = dryRun => new RunbookScriptResult(0, dryRun ? "{\"plan\":\"ok\"}" : "{\"done\":true}", string.Empty, false);

    public Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Enqueue((package.Manifest.Id, parameters, dryRun));
        return Task.FromResult(Result(dryRun));
    }
}

/// <summary>Otomasyon iş akışı: her test kendi veritabanı ve anahtarıyla çalışır.</summary>
public sealed class AutomationWorkflowTests : IDisposable
{
    private const string Ankara = "OU=ANKARA,OU=IL,DC=corp,DC=example";
    private const string Izmir = "OU=IZMIR,OU=IL,DC=corp,DC=example";
    private const string Script = "param([string]$UserDn, [switch]$DryRun)\n'ok'";

    private readonly FakeRunbookScriptHost _host = new();
    private readonly SystemPanelFactory _factory;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public AutomationWorkflowTests()
    {
        _factory = new SystemPanelFactory(s =>
        {
            s.RemoveAll<IRunbookScriptHost>();
            s.AddSingleton<IRunbookScriptHost>(_host);
        });
        _ = _factory.Server; // uygulamayı başlat (şema + tohum)
    }

    public void Dispose()
    {
        _factory.Dispose();
        _key.Dispose();
    }

    // ------------------------------------------------------------------ yardımcılar
    private static ClaimsPrincipal User(string name, string role, params string[] ouRoots)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.Role, role) };
        claims.AddRange(ouRoots.Select(r => new Claim(ScopeClaimTypes.OuRoot, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static readonly ClaimsPrincipal AnkaraOps1 = User("ops1", "AutoOps", Ankara);
    private static readonly ClaimsPrincipal AnkaraOps2 = User("ops2", "AutoOps", Ankara);
    private static readonly ClaimsPrincipal IzmirOps = User("ops3", "AutoOps", Izmir);

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private async Task SetUpAsync()
    {
        await WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var p in new[] { Permissions.AutomationView, Permissions.AutomationRun, Permissions.AutomationApprove })
                db.RolePermissions.Add(new RolePermission { RoleName = "AutoOps", Permission = p });
            await db.SaveChangesAsync();
            sp.GetRequiredService<IRolePermissionStore>().Invalidate();

            var added = await sp.GetRequiredService<RunbookTrustStore>().AddAsync("Test anahtarı", _key.ExportSubjectPublicKeyInfoPem(), "test");
            Assert.True(added.Ok, added.Error);
            return 0;
        });
    }

    private async Task<Guid> ImportAsync(string risk, string id = "test.user.disable", ECDsa? signer = null)
    {
        var manifest = new RunbookManifest
        {
            Id = id,
            Version = "1.0.0",
            Title = "Test runbook",
            TargetType = RunbookTargetTypes.AdUser,
            Risk = risk,
            SupportsDryRun = risk == RunbookRisks.High,
            Parameters = { new RunbookParameter { Name = "UserDn", Type = RunbookParameterTypes.AdDn } },
        };
        var package = RunbookPackageCodec.Sign(manifest, Script, signer ?? _key);
        var result = await WithScopeAsync(sp => sp.GetRequiredService<RunbookCatalog>().ImportAsync(package, "test"));
        Assert.True(result.Success, result.Error);
        return result.Version!.Id;
    }

    private Task<RunbookOperationResult> RequestAsync(ClaimsPrincipal user, Guid versionId, string userDn, string? justification = "test")
        => WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>()
            .RequestAsync(user, versionId, new Dictionary<string, string?> { ["UserDn"] = userDn }, justification));

    private Task<RunbookOperationResult> ApproveAsync(ClaimsPrincipal user, Guid runId)
        => WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>().ApproveAsync(user, runId, "ok"));

    private Task<bool> ExecuteNextAsync()
        => WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>().ExecuteNextAsync());

    private Task<RunbookRun> LoadAsync(Guid runId)
        => WithScopeAsync(async sp => (await sp.GetRequiredService<RunbookRunService>().LoadAsync(runId))!);

    private Task<List<string>> AuditActionsAsync(Guid runId)
        => WithScopeAsync(sp => sp.GetRequiredService<ApplicationDbContext>().AuditEvents
            .Where(e => e.ResourceId == runId.ToString()).OrderBy(e => e.Id).Select(e => e.Action).ToListAsync());

    // ------------------------------------------------------------------ testler
    [Fact]
    public async Task LowRisk_RunsWithoutApproval()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.Low);

        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);
        Assert.True(request.Success, request.Error);
        Assert.Equal(RunbookRunStatus.Approved, request.Run!.Status);

        Assert.True(await ExecuteNextAsync());
        var run = await LoadAsync(request.Run.Id);
        Assert.Equal(RunbookRunStatus.Succeeded, run.Status);

        var call = Assert.Single(_host.Calls);
        Assert.False(call.DryRun);
        Assert.Equal("CN=Ali," + Ankara, call.Parameters.Values["UserDn"]);
    }

    [Fact]
    public async Task HighRisk_PreviewThenFourEyesApproval()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.High);

        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);
        Assert.True(request.Success, request.Error);
        Assert.Equal(RunbookRunStatus.PendingApproval, request.Run!.Status);
        Assert.Contains("plan", request.Run.PreviewOutput);
        Assert.True(Assert.Single(_host.Calls).DryRun);

        // Onay olmadan çalışmaz.
        Assert.False(await ExecuteNextAsync());

        // Dört göz: talep eden kendi talebini onaylayamaz.
        var self = await ApproveAsync(AnkaraOps1, request.Run.Id);
        Assert.False(self.Success);
        Assert.Contains("dört göz", self.Error);

        var other = await ApproveAsync(AnkaraOps2, request.Run.Id);
        Assert.True(other.Success, other.Error);
        Assert.Equal(RunbookRunStatus.Approved, other.Run!.Status);

        Assert.True(await ExecuteNextAsync());
        Assert.Equal(RunbookRunStatus.Succeeded, (await LoadAsync(request.Run.Id)).Status);

        var actions = await AuditActionsAsync(request.Run.Id);
        Assert.Contains("automation.run.requested", actions);
        Assert.Contains("automation.run.approved", actions);
        Assert.Contains("automation.run.started", actions);
        Assert.Contains("automation.run.completed", actions);
    }

    [Fact]
    public async Task Request_OutsideScope_IsRejected()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.Low);

        var request = await RequestAsync(AnkaraOps1, version, "CN=Veli," + Izmir);

        Assert.False(request.Success);
        Assert.Contains(request.ValidationErrors!, e => e.Contains("kapsam"));
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task Approver_OutsideTargetScope_CannotApprove()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.High);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        var result = await ApproveAsync(IzmirOps, request.Run!.Id);

        Assert.False(result.Success);
        Assert.Contains("kapsam", result.Error);
        Assert.Equal(RunbookRunStatus.PendingApproval, (await LoadAsync(request.Run.Id)).Status);
    }

    [Fact]
    public async Task UserWithoutApprovePermission_CannotApprove()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.High);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        var result = await ApproveAsync(User("helpdesk1", "Helpdesk", Ankara), request.Run!.Id);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task FailedPreview_NeverExecutes()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.High);
        _host.Result = dryRun => new RunbookScriptResult(1, string.Empty, "Kullanıcı bulunamadı", false);

        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        Assert.Equal(RunbookRunStatus.PreviewFailed, request.Run!.Status);
        Assert.False((await ApproveAsync(AnkaraOps2, request.Run.Id)).Success);
        Assert.False(await ExecuteNextAsync());
    }

    [Fact]
    public async Task Rejected_NeverExecutes()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.High);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        var rejected = await WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>().RejectAsync(AnkaraOps2, request.Run!.Id, "hayır"));

        Assert.True(rejected.Success);
        Assert.Equal(RunbookRunStatus.Rejected, (await LoadAsync(request.Run!.Id)).Status);
        Assert.False(await ExecuteNextAsync());
    }

    [Fact]
    public async Task KeyRevokedAfterApproval_ExecutionFails()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.Low);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        await WithScopeAsync(async sp =>
        {
            var store = sp.GetRequiredService<RunbookTrustStore>();
            var key = (await store.ListAsync()).Single(k => !k.IsBuiltIn);
            return await store.RevokeAsync(key.Id!.Value, "test");
        });

        Assert.True(await ExecuteNextAsync());
        var run = await LoadAsync(request.Run!.Id);
        Assert.Equal(RunbookRunStatus.Failed, run.Status);
        Assert.Contains("güvenilir değil", run.Error);
        Assert.DoesNotContain(_host.Calls, c => !c.DryRun);
    }

    [Fact]
    public async Task PackageFromUntrustedKey_CannotBeImported()
    {
        await SetUpAsync();
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new RunbookManifest { Id = "evil.thing", Version = "1.0.0", Title = "x", Risk = RunbookRisks.Low };
        var package = RunbookPackageCodec.Sign(manifest, "'x'", attacker);

        var result = await WithScopeAsync(sp => sp.GetRequiredService<RunbookCatalog>().ImportAsync(package, "test"));

        Assert.False(result.Success);
        Assert.Contains("güvenilen", result.Error);
    }

    [Fact]
    public async Task SameVersionDifferentContent_IsRejected()
    {
        await SetUpAsync();
        await ImportAsync(RunbookRisks.Low);
        var manifest = new RunbookManifest
        {
            Id = "test.user.disable", Version = "1.0.0", Title = "Changed", Risk = RunbookRisks.Low,
            Parameters = { new RunbookParameter { Name = "UserDn", Type = RunbookParameterTypes.AdDn } },
        };
        var package = RunbookPackageCodec.Sign(manifest, Script + "\n# değişti", _key);

        var result = await WithScopeAsync(sp => sp.GetRequiredService<RunbookCatalog>().ImportAsync(package, "test"));

        Assert.False(result.Success);
        Assert.Contains("farklı içerikle", result.Error);
    }

    [Fact]
    public async Task ExpiredApproval_IsClosed()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.High);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        await WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var run = await db.RunbookRuns.SingleAsync(r => r.Id == request.Run!.Id);
            run.ApprovalExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });

        Assert.False((await ApproveAsync(AnkaraOps2, request.Run!.Id)).Success);
        Assert.Equal(1, await WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>().ExpirePendingAsync()));
        Assert.Equal(RunbookRunStatus.Expired, (await LoadAsync(request.Run.Id)).Status);
    }

    [Fact]
    public async Task Requester_CanCancel_BeforeExecution()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.Low);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        Assert.False((await WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>().CancelAsync(AnkaraOps2, request.Run!.Id))).Success);
        Assert.True((await WithScopeAsync(sp => sp.GetRequiredService<RunbookRunService>().CancelAsync(AnkaraOps1, request.Run!.Id))).Success);
        Assert.False(await ExecuteNextAsync());
    }

    [Fact]
    public async Task Output_IsRedacted()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.Low);
        _host.Result = _ => new RunbookScriptResult(0, "{\"user\":\"ali\",\"newPassword\":\"Gizli123!\"}\n", "token=abc123", false);
        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara);

        await ExecuteNextAsync();
        var run = await LoadAsync(request.Run!.Id);

        Assert.DoesNotContain("Gizli123!", run.Output);
        Assert.DoesNotContain("abc123", run.Output);
        Assert.Contains("ali", run.Output);
    }

    [Fact]
    public async Task MediumRisk_RequiresJustification()
    {
        await SetUpAsync();
        var version = await ImportAsync(RunbookRisks.Medium);

        var request = await RequestAsync(AnkaraOps1, version, "CN=Ali," + Ankara, justification: " ");

        Assert.False(request.Success);
        Assert.Contains("Gerekçe", request.Error);
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemPanel.Auditing;
using SystemPanel.Automation;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Security.Scope;

namespace SystemPanel.IntegrationTests;

/// <summary>ADR-002 Faz 4: kurum anahtarı yaşam döngüsü (iptal) ve runbook kimliği sahipliği.</summary>
public sealed class AutomationKeyLifecycleTests : IDisposable
{
    private const string Ankara = "OU=ANKARA,OU=IL,DC=corp,DC=example";

    private sealed class TestVendorKey : IVendorRunbookKeyProvider
    {
        public TestVendorKey(string pem) => Pem = pem;
        public string Pem { get; }
        public string? GetPublicKeyPem() => Pem;
    }

    private readonly FakeRunbookScriptHost _host = new();
    private readonly ECDsa _vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _org = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _org2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly SystemPanelFactory _factory;

    public AutomationKeyLifecycleTests()
    {
        var vendorPem = _vendor.ExportSubjectPublicKeyInfoPem();
        _factory = new SystemPanelFactory(s =>
        {
            s.RemoveAll<IRunbookScriptHost>();
            s.AddSingleton<IRunbookScriptHost>(_host);
            s.RemoveAll<IVendorRunbookKeyProvider>();
            s.AddSingleton<IVendorRunbookKeyProvider>(new TestVendorKey(vendorPem));
        });
        _ = _factory.Server;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _vendor.Dispose();
        _org.Dispose();
        _org2.Dispose();
    }

    private static ClaimsPrincipal User(string name)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, "AutoAdmin"),
            new Claim(ScopeClaimTypes.OuRoot, Ankara),
        }, "Test"));

    private static readonly ClaimsPrincipal Owner = User("owner");

    private async Task<T> Scoped<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<bool> AddOrgKeyAsync(ECDsa key, string name = "Kurum") => Scoped(async sp =>
    {
        var (ok, error) = await sp.GetRequiredService<RunbookTrustStore>().AddAsync(name, key.ExportSubjectPublicKeyInfoPem(), "t");
        Assert.True(ok, error);
        return ok;
    });

    private Task GrantAsync() => Scoped(async sp =>
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        foreach (var p in new[] { Permissions.AutomationView, Permissions.AutomationRun, Permissions.AutomationApprove, Permissions.AutomationManage })
            db.RolePermissions.Add(new RolePermission { RoleName = "AutoAdmin", Permission = p });
        await db.SaveChangesAsync();
        sp.GetRequiredService<IRolePermissionStore>().Invalidate();
        return 0;
    });

    private static string Package(ECDsa key, string id, string version = "1.0.0", string risk = RunbookRisks.Low)
        => RunbookPackageCodec.Sign(new RunbookManifest
        {
            Id = id, Version = version, Title = id, Risk = risk, TargetType = RunbookTargetTypes.AdOu,
            SupportsDryRun = risk == RunbookRisks.High,
            Parameters = { new RunbookParameter { Name = "SearchBase", Type = RunbookParameterTypes.AdDn } },
        }, risk == RunbookRisks.High ? "param($SearchBase,[switch]$DryRun)\n'ok'" : "param($SearchBase)\n'ok'", key);

    private Task<RunbookImportResult> ImportAsync(string package)
        => Scoped(sp => sp.GetRequiredService<RunbookCatalog>().ImportAsync(package, "t"));

    private Task<Guid> OrgKeyIdAsync(ECDsa key) => Scoped(async sp =>
    {
        var fingerprint = RunbookPackageCodec.Fingerprint(key);
        return (await sp.GetRequiredService<RunbookTrustStore>().ListAsync()).Single(k => k.Fingerprint == fingerprint).Id!.Value;
    });

    private Task<KeyRevocationResult> RevokeAsync(Guid keyId)
        => Scoped(sp => sp.GetRequiredService<RunbookKeyRevocationService>().RevokeAsync(keyId, "security-officer"));

    private static readonly Dictionary<string, string?> Input = new() { ["SearchBase"] = "OU=Bilgisayarlar," + Ankara };

    // ------------------------------------------------------------------ iptal

    [Fact]
    public async Task Revoke_CancelsPendingRuns_AndDisablesSchedules_WithAudit()
    {
        await GrantAsync();
        await AddOrgKeyAsync(_org);
        var low = (await ImportAsync(Package(_org, "org.test.low"))).Version!.Id;
        var medium = (await ImportAsync(Package(_org, "org.test.medium", risk: RunbookRisks.Medium))).Version!.Id;
        var vendorLow = (await ImportAsync(Package(_vendor, "vendor.test.low"))).Version!.Id;

        var queued = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().RequestAsync(Owner, low, Input, "test"));
        var pending = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().RequestAsync(Owner, medium, Input, "test"));
        var vendorRun = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().RequestAsync(Owner, vendorLow, Input, "test"));
        Assert.Equal(RunbookRunStatus.Approved, queued.Run!.Status);
        Assert.Equal(RunbookRunStatus.PendingApproval, pending.Run!.Status);

        var schedule = await Scoped(sp => sp.GetRequiredService<RunbookScheduleService>().CreateAsync(Owner, new ScheduleRequest
        {
            Name = "Gece", VersionId = low, Input = Input, TriggerType = RunbookTriggerTypes.Cron,
            CronExpression = "0 3 * * *", MinIntervalMinutes = 60, Justification = "test",
        }));
        Assert.True(schedule.Success, schedule.Error);

        var result = await RevokeAsync(await OrgKeyIdAsync(_org));

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.CancelledRuns);
        Assert.Equal(1, result.DisabledSchedules);

        await Scoped(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var q = await db.RunbookRuns.SingleAsync(r => r.Id == queued.Run.Id);
            Assert.Equal(RunbookRunStatus.Cancelled, q.Status);
            Assert.Contains("anahtar iptal", q.Error);
            Assert.Equal(RunbookRunStatus.Cancelled, (await db.RunbookRuns.SingleAsync(r => r.Id == pending.Run.Id)).Status);
            // Başka anahtarla imzalı talepler etkilenmez.
            Assert.Equal(RunbookRunStatus.Approved, (await db.RunbookRuns.SingleAsync(r => r.Id == vendorRun.Run!.Id)).Status);

            var s = await db.RunbookSchedules.SingleAsync(x => x.Id == schedule.Schedule!.Id);
            Assert.Equal(RunbookScheduleStatus.Disabled, s.Status);
            Assert.Null(s.NextFireAtUtc);

            var audit = await db.AuditEvents.AsNoTracking().ToListAsync();
            Assert.Contains(audit, a => a.Action == AuditActions.AutomationKeyRevoke);
            Assert.Equal(2, audit.Count(a => a.Action == AuditActions.AutomationRunCancelled && a.ActorUserName == "security-officer"));
            Assert.Contains(audit, a => a.Action == AuditActions.AutomationScheduleDisable && a.ResourceId == s.Id.ToString());
            return 0;
        });

        // Kuyrukta kalan tek iş başka anahtarın işi; iptal edilen iş çalışmaz.
        Assert.True(await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ExecuteNextAsync()));
        Assert.False(await Scoped(sp => sp.GetRequiredService<RunbookRunService>().ExecuteNextAsync()));
        Assert.Equal(new[] { "vendor.test.low" }, _host.Calls.Select(c => c.RunbookId).ToArray());
    }

    [Fact]
    public async Task RevokedKey_CannotImport_NorRequest()
    {
        await GrantAsync();
        await AddOrgKeyAsync(_org);
        var version = (await ImportAsync(Package(_org, "org.test.low"))).Version!.Id;
        Assert.True((await RevokeAsync(await OrgKeyIdAsync(_org))).Success);

        Assert.False((await ImportAsync(Package(_org, "org.test.low", "1.0.1"))).Success);
        var request = await Scoped(sp => sp.GetRequiredService<RunbookRunService>().RequestAsync(Owner, version, Input, "test"));
        Assert.False(request.Success);
        Assert.Contains("güvenilir değil", request.Error);
    }

    [Fact]
    public async Task Revoke_Twice_Or_UnknownKey_Fails()
    {
        await AddOrgKeyAsync(_org);
        var id = await OrgKeyIdAsync(_org);
        Assert.True((await RevokeAsync(id)).Success);
        Assert.False((await RevokeAsync(id)).Success);
        Assert.False((await RevokeAsync(Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task PrivateKeyPem_IsRefused()
    {
        var (ok, error) = await Scoped(sp => sp.GetRequiredService<RunbookTrustStore>().AddAsync("Yanlış", _org.ExportPkcs8PrivateKeyPem(), "t"));
        Assert.False(ok);
        Assert.Contains("ÖZEL", error);
        Assert.Empty(await Scoped(async sp => await sp.GetRequiredService<ApplicationDbContext>().RunbookSigningKeys.ToListAsync()));
    }

    // ------------------------------------------------------------------ sahiplik

    [Fact]
    public async Task OrgKey_CannotPublishNewVersionOfVendorRunbook()
    {
        await AddOrgKeyAsync(_org);
        Assert.True((await ImportAsync(Package(_vendor, "ad.user.offboard"))).Success);

        var hijack = await ImportAsync(Package(_org, "ad.user.offboard", "9.9.9", RunbookRisks.Low));

        Assert.False(hijack.Success);
        Assert.Contains("üretici kataloğuna ait", hijack.Error);
    }

    [Fact]
    public async Task VendorKey_CannotUseOrganizationNamespace()
    {
        var result = await ImportAsync(Package(_vendor, "org.kurum.report"));
        Assert.False(result.Success);
        Assert.Contains("kurum runbook'larına ayrılmıştır", result.Error);
    }

    [Fact]
    public async Task VendorUpdate_CannotOverrideOrganizationRunbook()
    {
        await AddOrgKeyAsync(_org);
        Assert.True((await ImportAsync(Package(_org, "custom.report"))).Success);
        Assert.False((await ImportAsync(Package(_vendor, "custom.report", "2.0.0"))).Success);
    }

    [Fact]
    public async Task OrganizationKeyRotation_IsAllowed()
    {
        await AddOrgKeyAsync(_org, "Kurum 2026");
        Assert.True((await ImportAsync(Package(_org, "org.kurum.report"))).Success);
        Assert.True((await RevokeAsync(await OrgKeyIdAsync(_org))).Success);

        await AddOrgKeyAsync(_org2, "Kurum 2027");
        var rotated = await ImportAsync(Package(_org2, "org.kurum.report", "1.1.0"));

        Assert.True(rotated.Success, rotated.Error);
    }
}

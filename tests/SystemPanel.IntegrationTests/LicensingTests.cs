using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemPanel.Contracts.Agents;
using SystemPanel.Contracts.Licensing;
using SystemPanel.Licensing;
using SystemPanel.Services.Agents;

namespace SystemPanel.IntegrationTests;

/// <summary>Doğrulama anahtarı gömülü bir "ürün yapısı" gibi davranan uygulama.</summary>
public sealed class LicensedFactory : IDisposable
{
    public ECDsa VendorKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public string LicensePath { get; } = Path.Combine(Path.GetTempPath(), "sp-license-" + Guid.NewGuid().ToString("N") + ".lic");
    public SystemPanelFactory Factory { get; }

    public LicensedFactory()
    {
        var pem = VendorKey.ExportSubjectPublicKeyInfoPem();
        Factory = new SystemPanelFactory(services =>
        {
            services.RemoveAll<ILicensePublicKeyProvider>();
            services.AddSingleton<ILicensePublicKeyProvider>(new StaticKey(pem));
            services.Configure<LicensingOptions>(o => o.LicenseFilePath = LicensePath);
        });
    }

    public void Dispose()
    {
        Factory.Dispose();
        VendorKey.Dispose();
        if (File.Exists(LicensePath)) File.Delete(LicensePath);
    }

    private sealed class StaticKey : ILicensePublicKeyProvider
    {
        private readonly string _pem;
        public StaticKey(string pem) => _pem = pem;
        public string? GetPublicKeyPem() => _pem;
    }
}

public sealed class LicensingTests : IClassFixture<LicensedFactory>
{
    private readonly LicensedFactory _fx;

    public LicensingTests(LicensedFactory fx) => _fx = fx;

    [Fact]
    public async Task LicensePage_RequiresSettingsView()
    {
        var anonymous = await _fx.Factory.CreateAnonymousClient().GetAsync("/License/Index");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);

        var helpdesk = await _fx.Factory.CreateUserClient("Helpdesk").GetAsync("/License/Index");
        Assert.Equal(HttpStatusCode.Forbidden, helpdesk.StatusCode);

        var admin = await _fx.Factory.CreateUserClient("Admin").GetAsync("/License/Index");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task AgentLimit_IsEnforcedFromLicense_ExistingAgentsKeepWorking()
    {
        File.WriteAllText(_fx.LicensePath, LicenseCodec.Sign(new LicenseInfo
        {
            LicenseId = "SP-IT",
            Customer = "Entegrasyon",
            IssuedUtc = DateTime.UtcNow,
            MaxAgents = 2,
        }, _fx.VendorKey));
        File.SetLastWriteTimeUtc(_fx.LicensePath, DateTime.UtcNow.AddSeconds(1));

        using var scope = _fx.Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<AgentRegistry>();

        var first = await registry.RegisterAsync("LIC-SRV-1", null, null, "it");
        await registry.RegisterAsync("LIC-SRV-2", null, null, "it");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.RegisterAsync("LIC-SRV-3", null, null, "it"));
        Assert.Contains("Lisans", ex.Message);

        // Sınıra ulaşılması mevcut agent'ların veri göndermesini engellemez.
        var client = _fx.Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(AgentApi.AgentIdHeader, first.AgentId.ToString());
        client.DefaultRequestHeaders.Add(AgentApi.AgentKeyHeader, first.Key);
        var response = await client.PostAsJsonAsync("/" + AgentApi.HeartbeatPath,
            new AgentHeartbeatRequest { Hostname = "LIC-SRV-1", AgentVersion = "2.0.0", SentAtUtc = DateTime.UtcNow });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

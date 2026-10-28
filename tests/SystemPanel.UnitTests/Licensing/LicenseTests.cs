using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Licensing;
using SystemPanel.Licensing;

namespace SystemPanel.UnitTests.Licensing;

public sealed class LicenseCodecTests
{
    private static LicenseInfo Sample() => new()
    {
        LicenseId = "SP-TEST-1",
        Customer = "Test Kurumu",
        Edition = LicenseEditions.Enterprise,
        IssuedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        MaxAgents = 50,
        Features = { LicenseFeatures.ActiveDirectory, LicenseFeatures.Monitoring },
    };

    [Fact]
    public void SignedLicense_Verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = LicenseCodec.Sign(Sample(), key);

        Assert.True(LicenseCodec.TryVerify(text, key, out var license, out var error), error);
        Assert.Equal("Test Kurumu", license!.Customer);
        Assert.Equal(50, license.MaxAgents);
    }

    [Fact]
    public void WrongKey_IsRejected()
    {
        using var vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = LicenseCodec.Sign(Sample(), attacker);

        Assert.False(LicenseCodec.TryVerify(forged, vendor, out _, out _));
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = LicenseCodec.Sign(Sample(), key);

        using var doc = JsonDocument.Parse(text);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(doc.RootElement.GetProperty("payload").GetString()!));
        var tampered = payload.Replace("\"maxAgents\":50", "\"maxAgents\":5000");
        Assert.NotEqual(payload, tampered);

        var forged = JsonSerializer.Serialize(new
        {
            alg = LicenseCodec.Algorithm,
            payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(tampered)),
            signature = doc.RootElement.GetProperty("signature").GetString(),
        });

        Assert.False(LicenseCodec.TryVerify(forged, key, out _, out var error));
        Assert.Contains("imza", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"alg\":\"none\",\"payload\":\"\",\"signature\":\"\"}")]
    [InlineData("{\"alg\":\"ES256\",\"payload\":\"***\",\"signature\":\"***\"}")]
    public void Garbage_IsRejected(string text)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(LicenseCodec.TryVerify(text, key, out _, out _));
    }

    [Fact]
    public void Evaluate_ValidGraceExpired()
    {
        var license = Sample();
        var expires = license.ExpiresUtc!.Value;

        Assert.Equal(LicenseState.Valid, LicenseEvaluator.Evaluate(license, expires.AddDays(-1)).State);
        Assert.Equal(LicenseState.GracePeriod, LicenseEvaluator.Evaluate(license, expires.AddDays(10)).State);
        Assert.Equal(LicenseState.Expired, LicenseEvaluator.Evaluate(license, expires.AddDays(31)).State);

        license.ExpiresUtc = null;
        Assert.Equal(LicenseState.Valid, LicenseEvaluator.Evaluate(license, DateTime.UtcNow.AddYears(50)).State);
    }
}

public sealed class LicenseServiceTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private string LicensePath => Path.Combine(_dir, "license.lic");

    public void Dispose()
    {
        _key.Dispose();
        Directory.Delete(_dir, true);
    }

    private sealed class FixedKey : ILicensePublicKeyProvider
    {
        private readonly string? _pem;
        public FixedKey(string? pem) => _pem = pem;
        public string? GetPublicKeyPem() => _pem;
    }

    private LicenseService Service(string? pem)
        => new(new FixedKey(pem), Options.Create(new LicensingOptions { LicenseFilePath = LicensePath }), NullLogger<LicenseService>.Instance);

    private string Sign(int? maxAgents, DateTime? expires = null) => LicenseCodec.Sign(new LicenseInfo
    {
        LicenseId = "SP-1",
        Customer = "Kurum",
        IssuedUtc = DateTime.UtcNow,
        ExpiresUtc = expires,
        MaxAgents = maxAgents,
    }, _key);

    [Fact]
    public void NoEmbeddedKey_IsNotConfigured_AndUnlimited()
    {
        var service = Service(null);
        Assert.Equal(LicenseState.NotConfigured, service.GetStatus().State);
        Assert.Null(service.MaxAgents);
        Assert.False(service.GetStatus().ShowWarning);
    }

    [Fact]
    public void MissingFile_AppliesEvaluationLimit()
    {
        var service = Service(_key.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(LicenseState.Missing, service.GetStatus().State);
        Assert.Equal(LicenseLimits.UnlicensedMaxAgents, service.MaxAgents);
    }

    [Fact]
    public async Task Install_ValidLicense_PersistsAndApplies()
    {
        var service = Service(_key.ExportSubjectPublicKeyInfoPem());

        var status = await service.InstallAsync(Sign(maxAgents: 250));

        Assert.Equal(LicenseState.Valid, status.State);
        Assert.Equal(250, service.MaxAgents);
        Assert.True(File.Exists(LicensePath));

        // Yeni örnek (uygulama yeniden başlatıldı) dosyadan okur.
        Assert.Equal(250, Service(_key.ExportSubjectPublicKeyInfoPem()).MaxAgents);
    }

    [Fact]
    public async Task Install_ForgedLicense_IsRejected_AndNothingWritten()
    {
        var service = Service(_key.ExportSubjectPublicKeyInfoPem());
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = LicenseCodec.Sign(new LicenseInfo { LicenseId = "X", Customer = "Y", IssuedUtc = DateTime.UtcNow }, other);

        var status = await service.InstallAsync(forged);

        Assert.Equal(LicenseState.Invalid, status.State);
        Assert.False(File.Exists(LicensePath));
    }

    [Fact]
    public void ExpiredBeyondGrace_FallsBackToEvaluationLimit()
    {
        File.WriteAllText(LicensePath, Sign(maxAgents: 1000, expires: DateTime.UtcNow.AddDays(-60)));
        var service = Service(_key.ExportSubjectPublicKeyInfoPem());

        Assert.Equal(LicenseState.Expired, service.GetStatus().State);
        Assert.Equal(LicenseLimits.UnlicensedMaxAgents, service.MaxAgents);
    }

    [Fact]
    public void CorruptFile_IsInvalid_NotException()
    {
        File.WriteAllText(LicensePath, "çöp");
        Assert.Equal(LicenseState.Invalid, Service(_key.ExportSubjectPublicKeyInfoPem()).GetStatus().State);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SystemPanel.Contracts.Automation;

namespace SystemPanel.UnitTests.Automation;

public sealed class RunbookPackageCodecTests
{
    internal static RunbookManifest Manifest() => new()
    {
        Id = "test.user.disable",
        Version = "1.0.0",
        Title = "Test",
        TargetType = RunbookTargetTypes.AdUser,
        Risk = RunbookRisks.High,
        SupportsDryRun = true,
        Parameters =
        {
            new RunbookParameter { Name = "UserDn", Type = RunbookParameterTypes.AdDn },
            new RunbookParameter { Name = "Reason", Type = RunbookParameterTypes.String, MaxLength = 50, Required = false },
        },
    };

    private const string Script = "param([string]$UserDn, [switch]$DryRun)\n'ok'";

    [Fact]
    public void SignedPackage_Verifies_AndExposesScript()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = RunbookPackageCodec.Sign(Manifest(), Script, key);

        Assert.True(RunbookPackageCodec.TryVerify(text, key, out var pkg, out var error), error);
        Assert.Equal("test.user.disable", pkg!.Manifest.Id);
        Assert.Equal(Script, pkg.Script);
        Assert.Equal(RunbookPackageCodec.Fingerprint(key), pkg.KeyFingerprint);
    }

    [Fact]
    public void OtherKey_IsRejected()
    {
        using var vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = RunbookPackageCodec.Sign(Manifest(), Script, attacker);

        Assert.False(RunbookPackageCodec.TryVerify(text, vendor, out _, out _));
    }

    [Fact]
    public void TamperedScript_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = RunbookPackageCodec.Sign(Manifest(), Script, key);

        var node = JsonDocument.Parse(text).RootElement;
        var tampered = JsonSerializer.Serialize(new
        {
            format = node.GetProperty("format").GetString(),
            keyId = node.GetProperty("keyId").GetString(),
            manifest = node.GetProperty("manifest").GetString(),
            script = Convert.ToBase64String(Encoding.UTF8.GetBytes(Script + "\nRemove-Item C:\\ -Recurse")),
            signature = node.GetProperty("signature").GetString(),
        });

        Assert.False(RunbookPackageCodec.TryVerify(tampered, key, out _, out var error));
        Assert.Contains("özeti", error);
    }

    [Fact]
    public void TamperedManifest_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = RunbookPackageCodec.Sign(Manifest(), Script, key);

        var node = JsonDocument.Parse(text).RootElement;
        var manifestJson = Encoding.UTF8.GetString(Convert.FromBase64String(node.GetProperty("manifest").GetString()!));
        var lowered = manifestJson.Replace("\"risk\":\"high\"", "\"risk\":\"low\"");
        Assert.NotEqual(manifestJson, lowered);

        var tampered = JsonSerializer.Serialize(new
        {
            format = "sprb/1",
            keyId = node.GetProperty("keyId").GetString(),
            manifest = Convert.ToBase64String(Encoding.UTF8.GetBytes(lowered)),
            script = node.GetProperty("script").GetString(),
            signature = node.GetProperty("signature").GetString(),
        });

        Assert.False(RunbookPackageCodec.TryVerify(tampered, key, out _, out var error));
        Assert.Contains("imza", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"other\",\"keyId\":\"x\"}")]
    [InlineData("not json")]
    public void Garbage_IsRejected(string text)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(RunbookPackageCodec.TryVerify(text, key, out _, out _));
    }

    [Fact]
    public void Validator_RejectsUnsafeManifests()
    {
        var m = Manifest();
        m.ScriptSha256 = new string('a', 64);
        Assert.Empty(RunbookManifestValidator.Validate(m));

        m.Risk = RunbookRisks.High;
        m.SupportsDryRun = false;
        Assert.Contains(RunbookManifestValidator.Validate(m), e => e.Contains("önizleme"));

        m = Manifest();
        m.ScriptSha256 = new string('a', 64);
        m.Parameters.Add(new RunbookParameter { Name = "DryRun", Type = RunbookParameterTypes.Bool });
        Assert.Contains(RunbookManifestValidator.Validate(m), e => e.Contains("ayrılmış"));

        m = Manifest();
        m.ScriptSha256 = new string('a', 64);
        m.Executor = "anywhere";
        m.Id = "Bad Id";
        Assert.True(RunbookManifestValidator.Validate(m).Count >= 2);
    }
}

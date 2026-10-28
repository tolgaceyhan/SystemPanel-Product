using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystemPanel.Contracts.Automation;

namespace SystemPanel.UnitTests.Automation;

/// <summary>runbooks/src altındaki yerleşik runbook kaynakları: manifest geçerli, betik parametreleri manifestle uyumlu, imzalanabilir.</summary>
public sealed class BuiltInRunbooksTests
{
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SystemPanel.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "runbooks", "src");
    }

    public static IEnumerable<object[]> Runbooks()
        => Directory.GetDirectories(SourceRoot()).Select(d => new object[] { Path.GetFileName(d) });

    private static (RunbookManifest Manifest, string Script) Load(string name)
    {
        var dir = Path.Combine(SourceRoot(), name);
        var manifest = JsonSerializer.Deserialize<RunbookManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return (manifest, File.ReadAllText(Path.Combine(dir, "run.ps1")));
    }

    [Fact]
    public void AtLeastThreeBuiltIns()
        => Assert.True(Runbooks().Count() >= 3);

    [Theory]
    [MemberData(nameof(Runbooks))]
    public void Manifest_IsValid_AndMatchesFolder(string name)
    {
        var (manifest, _) = Load(name);
        Assert.Empty(RunbookManifestValidator.Validate(manifest, requireScriptHash: false));
        Assert.Equal(name, manifest.Id);
    }

    [Theory]
    [MemberData(nameof(Runbooks))]
    public void Script_DeclaresManifestParameters(string name)
    {
        var (manifest, script) = Load(name);
        var paramBlock = Regex.Match(script, @"param\s*\((?<body>.*?)\)\s*\n\s*\n", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups["body"].Value;
        Assert.False(string.IsNullOrWhiteSpace(paramBlock), "param() bloğu bulunamadı.");

        foreach (var p in manifest.Parameters)
        {
            Assert.Matches(new Regex(@"\$" + p.Name + @"\b", RegexOptions.IgnoreCase), paramBlock);
            if (p.Type == RunbookParameterTypes.Bool)
                Assert.Matches(new Regex(@"\[switch\]\s*\$" + p.Name + @"\b", RegexOptions.IgnoreCase), paramBlock);
        }

        var declaresDryRun = Regex.IsMatch(paramBlock, @"\[switch\]\s*\$DryRun\b", RegexOptions.IgnoreCase);
        Assert.Equal(manifest.SupportsDryRun, declaresDryRun);
    }

    [Theory]
    [MemberData(nameof(Runbooks))]
    public void Script_HasNoDangerousConstructs(string name)
    {
        var (_, script) = Load(name);
        foreach (var pattern in new[] { @"Invoke-Expression", @"\biex\b", @"-EncodedCommand", @"Start-Process", @"DownloadString", @"Invoke-WebRequest", @"Invoke-RestMethod" })
            Assert.DoesNotMatch(new Regex(pattern, RegexOptions.IgnoreCase), script);
    }

    [Theory]
    [MemberData(nameof(Runbooks))]
    public void Lint_HasNoErrors_WithVendorRules(string name)
    {
        var findings = RunbookSource.Lint(RunbookSource.Load(Path.Combine(SourceRoot(), name)), organization: false);
        Assert.Empty(findings.Where(f => f.Severity == RunbookLintSeverity.Error).Select(f => f.Message));
    }

    [Theory]
    [MemberData(nameof(Runbooks))]
    public void CanBeSignedAndVerified(string name)
    {
        var (manifest, script) = Load(name);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = RunbookPackageCodec.Sign(manifest, script, key);
        Assert.True(RunbookPackageCodec.TryVerify(package, key, out _, out var error), error);
    }
}

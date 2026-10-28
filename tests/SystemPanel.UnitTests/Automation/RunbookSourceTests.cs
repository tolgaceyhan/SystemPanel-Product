using SystemPanel.Contracts.Automation;

namespace SystemPanel.UnitTests.Automation;

/// <summary>Runbook kaynak denetimi (imzalama aracının imzalamadan önce uyguladığı kurallar).</summary>
public sealed class RunbookSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sp-rb-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private const string GoodManifest = """
        {
          "id": "org.kurum.disk.report",
          "version": "1.0.0",
          "title": "Disk raporu",
          "description": "Disk doluluk raporu üretir.",
          "executor": "agent",
          "targetType": "server",
          "risk": "low",
          "timeoutSeconds": 60,
          "parameters": [ { "name": "Drive", "type": "string", "pattern": "[A-Z]:" } ]
        }
        """;

    private const string GoodScript = "param(\n    [string]$Drive\n)\n\n$ErrorActionPreference = 'Stop'\n'ok'\n";

    private RunbookSourceDirectory Write(string folder, string manifest, string script)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ManifestFile), manifest);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ScriptFile), script);
        return RunbookSource.Load(dir);
    }

    private static string[] Errors(IReadOnlyList<RunbookLintFinding> findings)
        => findings.Where(f => f.Severity == RunbookLintSeverity.Error).Select(f => f.Message).ToArray();

    [Fact]
    public void GoodOrganizationRunbook_HasNoFindings()
        => Assert.Empty(RunbookSource.Lint(Write("org.kurum.disk.report", GoodManifest, GoodScript), organization: true));

    [Fact]
    public void FolderMustMatchId()
        => Assert.Contains(Errors(RunbookSource.Lint(Write("baska-ad", GoodManifest, GoodScript), true)), e => e.Contains("Klasör adı"));

    [Fact]
    public void OrganizationRunbook_RequiresOrgPrefix()
    {
        var manifest = GoodManifest.Replace("org.kurum.disk.report", "kurum.disk.report");
        Assert.Contains(Errors(RunbookSource.Lint(Write("kurum.disk.report", manifest, GoodScript), true)), e => e.Contains("org."));
    }

    [Fact]
    public void VendorRunbook_CannotUseOrgPrefix()
        => Assert.Contains(Errors(RunbookSource.Lint(Write("org.kurum.disk.report", GoodManifest, GoodScript), false)), e => e.Contains("ayrılmıştır"));

    [Fact]
    public void MissingParameterInScript_IsError()
    {
        var script = "param(\n    [string]$Other\n)\n\n$ErrorActionPreference = 'Stop'\n";
        var findings = RunbookSource.Lint(Write("org.kurum.disk.report", GoodManifest, script), true);
        Assert.Contains(Errors(findings), e => e.Contains("'Drive'"));
        Assert.Contains(findings, f => f.Severity == RunbookLintSeverity.Warning && f.Message.Contains("'Other'"));
    }

    [Fact]
    public void DryRunMismatch_IsError()
    {
        var script = "param(\n    [string]$Drive,\n    [switch]$DryRun\n)\n\n$ErrorActionPreference = 'Stop'\n";
        Assert.Contains(Errors(RunbookSource.Lint(Write("org.kurum.disk.report", GoodManifest, script), true)), e => e.Contains("SupportsDryRun=false"));
    }

    [Theory]
    [InlineData("Invoke-Expression $x")]
    [InlineData("iex $x")]
    [InlineData("[ScriptBlock]::Create($x)")]
    [InlineData("Start-Process notepad")]
    [InlineData("cmd /c dir")]
    [InlineData("(New-Object Net.WebClient).DownloadString('https://x.example.invalid')")]
    [InlineData("Invoke-WebRequest https://x.example.invalid")]
    [InlineData("Add-Type -TypeDefinition $code")]
    [InlineData("$password = '<placeholder>'")]
    public void DangerousConstructs_AreErrors(string line)
    {
        var findings = RunbookSource.Lint(Write("org.kurum.disk.report", GoodManifest, GoodScript + line + "\n"), true);
        Assert.NotEmpty(Errors(findings));
    }

    [Fact]
    public void BoolParameter_MustBeSwitch()
    {
        var manifest = GoodManifest.Replace("""{ "name": "Drive", "type": "string", "pattern": "[A-Z]:" }""", """{ "name": "Force", "type": "bool" }""");
        var script = "param(\n    [bool]$Force\n)\n\n$ErrorActionPreference = 'Stop'\n";
        Assert.Contains(Errors(RunbookSource.Lint(Write("org.kurum.disk.report", manifest, script), true)), e => e.Contains("[switch]"));
    }

    [Fact]
    public void StringWithoutPattern_AndMissingStrictness_AreWarnings()
    {
        var manifest = GoodManifest.Replace(""", "pattern": "[A-Z]:" """, " ");
        var findings = RunbookSource.Lint(Write("org.kurum.disk.report", manifest, "param(\n    [string]$Drive\n)\n\n'ok'\n"), true);
        Assert.Empty(Errors(findings));
        Assert.Contains(findings, f => f.Message.Contains("Pattern"));
        Assert.Contains(findings, f => f.Message.Contains("ErrorActionPreference"));
    }

    [Fact]
    public void InvalidJson_Throws()
    {
        var dir = Path.Combine(_root, "x");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ManifestFile), "{ not json");
        File.WriteAllText(Path.Combine(dir, RunbookSource.ScriptFile), GoodScript);
        Assert.Throws<FormatException>(() => RunbookSource.Load(dir));
    }

    [Fact]
    public void FindDirectories_ReturnsOnlyFoldersWithManifest()
    {
        Write("org.kurum.disk.report", GoodManifest, GoodScript);
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        Assert.Single(RunbookSource.FindDirectories(_root));
    }
}

public sealed class RunbookTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sp-rbt-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(RunbookExecutors.Server, RunbookRisks.Low)]
    [InlineData(RunbookExecutors.Server, RunbookRisks.High)]
    [InlineData(RunbookExecutors.Agent, RunbookRisks.Low)]
    [InlineData(RunbookExecutors.Agent, RunbookRisks.Medium)]
    public void Template_PassesLint_AndCanBeSigned(string executor, string risk)
    {
        const string id = "org.kurum.sample";
        var (manifest, script) = RunbookTemplate.Create(id, executor, risk);
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ManifestFile), manifest);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ScriptFile), script);

        var source = RunbookSource.Load(dir);
        Assert.Equal(risk != RunbookRisks.Low, source.Manifest.SupportsDryRun);
        Assert.Empty(RunbookSource.Lint(source, organization: true).Where(f => f.Severity == RunbookLintSeverity.Error).Select(f => f.Message));

        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var package = RunbookSource.Sign(source, key, organization: true, out _);
        Assert.True(RunbookPackageCodec.TryVerify(package, key, out _, out var error), error);
    }

    [Fact]
    public void Sign_RefusesSourceWithLintErrors()
    {
        var (manifest, script) = RunbookTemplate.Create("org.kurum.sample", RunbookExecutors.Server, RunbookRisks.Low);
        var dir = Path.Combine(_root, "org.kurum.sample");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ManifestFile), manifest);
        File.WriteAllText(Path.Combine(dir, RunbookSource.ScriptFile), script + "Invoke-Expression $x\n");

        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var ex = Assert.Throws<ArgumentException>(() => RunbookSource.Sign(RunbookSource.Load(dir), key, organization: true, out _));
        Assert.Contains("imzalanmadı", ex.Message);
    }

    [Fact]
    public void Template_RejectsUnknownExecutorOrRisk()
    {
        Assert.Throws<ArgumentException>(() => RunbookTemplate.Create("org.a.b", "winrm", RunbookRisks.Low));
        Assert.Throws<ArgumentException>(() => RunbookTemplate.Create("org.a.b", RunbookExecutors.Server, "critical"));
    }
}

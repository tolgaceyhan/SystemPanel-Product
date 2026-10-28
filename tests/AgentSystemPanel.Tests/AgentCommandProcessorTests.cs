using System.Security.Cryptography;
using AgentSystemPanel;
using AgentSystemPanel.Automation;
using Microsoft.Extensions.Logging.Abstractions;
using SystemPanel.Contracts.Agents;
using SystemPanel.Contracts.Automation;

namespace AgentSystemPanel.Tests;

public sealed class AgentCommandProcessorTests : IDisposable
{
    private readonly ECDsa _vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeRunner _runner = new();

    public void Dispose() => _vendor.Dispose();

    private sealed class FakeRunner : IAgentScriptRunner
    {
        public List<(string Id, BoundRunbookParameters P, bool DryRun, TimeSpan Timeout)> Calls { get; } = new();
        public RunbookScriptResult Result { get; set; } = new(0, "{\"ok\":true}", string.Empty, false);

        public Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add((package.Manifest.Id, parameters, dryRun, timeout));
            return Task.FromResult(Result);
        }
    }

    private static RunbookManifest Manifest(string id = "server.service.restart", string executor = RunbookExecutors.Agent, bool dryRun = true) => new()
    {
        Id = id,
        Version = "1.0.0",
        Title = "t",
        Executor = executor,
        TargetType = executor == RunbookExecutors.Agent ? RunbookTargetTypes.Server : RunbookTargetTypes.None,
        Risk = RunbookRisks.Medium,
        SupportsDryRun = dryRun,
        TimeoutSeconds = 120,
        Parameters = { new RunbookParameter { Name = "ServiceName", Type = RunbookParameterTypes.String, Pattern = "[A-Za-z0-9_.-]{1,80}" } },
    };

    private AgentCommand Command(RunbookManifest? manifest = null, ECDsa? signer = null, string service = "Spooler", bool dryRun = false, int timeout = 600)
        => new()
        {
            RunId = Guid.NewGuid(),
            Package = RunbookPackageCodec.Sign(manifest ?? Manifest(), "param([string]$ServiceName,[switch]$DryRun)\n'ok'", signer ?? _vendor),
            Values = new() { ["ServiceName"] = service },
            DryRun = dryRun,
            TimeoutSeconds = timeout,
        };

    private AgentCommandProcessor Processor(params string[] allowed)
        => new(new AgentAutomationOptions { Enabled = true, AllowedRunbooks = allowed.ToList(), MaxOutputBytes = 1024 },
            new AgentTrustedKeys(new[] { _vendor.ExportSubjectPublicKeyInfoPem() }), _runner, NullLogger.Instance);

    [Fact]
    public async Task AllowedAndSigned_Runs_WithManifestTimeoutCap()
    {
        var result = await Processor("server.service.restart").ProcessAsync(Command(), CancellationToken.None);

        Assert.False(result.RejectedByAgent);
        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("Spooler", call.P.Values["ServiceName"]);
        Assert.Equal(TimeSpan.FromSeconds(120), call.Timeout); // sunucu 600 istese de manifest sınırı
    }

    [Fact]
    public async Task NotInLocalAllowlist_IsRejected_NotRun()
    {
        var result = await Processor("server.diagnostics.snapshot").ProcessAsync(Command(), CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Contains("izin listesinde değil", result.StdErr);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task SignedByUntrustedKey_IsRejected()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = await Processor("server.service.restart").ProcessAsync(Command(signer: attacker), CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ServerRunbook_IsRejected()
    {
        var m = Manifest(id: "ad.user.offboard", executor: RunbookExecutors.Server);
        m.Risk = RunbookRisks.High;
        var result = await Processor("ad.user.offboard").ProcessAsync(Command(m), CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Empty(_runner.Calls);
    }

    [Theory]
    [InlineData("-Command Stop-Computer")]
    [InlineData("Spooler; Stop-Computer")]
    [InlineData("a\"b")]
    public async Task InjectedParameters_AreRejected(string value)
    {
        var result = await Processor("server.service.restart").ProcessAsync(Command(service: value), CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ExtraParameter_IsRejected()
    {
        var command = Command();
        command.Values["Extra"] = "x";

        var result = await Processor("server.service.restart").ProcessAsync(command, CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task DryRunOnRunbookWithoutPreview_IsRejected()
    {
        var m = Manifest(dryRun: false);
        var result = await Processor("server.service.restart").ProcessAsync(Command(m, dryRun: true), CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task TamperedPackage_IsRejected()
    {
        var command = Command();
        command.Package = command.Package.Replace("\"script\": \"", "\"script\": \"QQ"); // betik base64'ü bozulur

        var result = await Processor("server.service.restart").ProcessAsync(command, CancellationToken.None);

        Assert.True(result.RejectedByAgent);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task LargeOutput_IsTruncated()
    {
        _runner.Result = new RunbookScriptResult(0, new string('x', 10_000), string.Empty, false);
        var result = await Processor("server.service.restart").ProcessAsync(Command(), CancellationToken.None);

        Assert.True(result.StdOut.Length < 1100);
        Assert.Contains("kısalttı", result.StdOut);
    }

    [Fact]
    public void AutomationIsDisabledByDefault()
        => Assert.False(new AgentOptions().Automation.Enabled);
}

using SystemPanel.Contracts.Automation;

namespace SystemPanel.UnitTests.Automation;

public sealed class AgentRunbookRulesTests
{
    private static RunbookManifest AgentManifest() => new()
    {
        Id = "server.service.restart",
        Version = "1.0.0",
        Title = "Servis yeniden başlat",
        Executor = RunbookExecutors.Agent,
        TargetType = RunbookTargetTypes.Server,
        Risk = RunbookRisks.Medium,
        ScriptSha256 = new string('a', 64),
        Parameters = { new RunbookParameter { Name = "ServiceName", Type = RunbookParameterTypes.String, Pattern = "[A-Za-z0-9_.-]{1,80}" } },
    };

    [Fact]
    public void ValidAgentManifest_Passes()
        => Assert.Empty(RunbookManifestValidator.Validate(AgentManifest()));

    [Fact]
    public void AgentRunbook_MustTargetServer()
    {
        var m = AgentManifest();
        m.TargetType = RunbookTargetTypes.AdUser;
        Assert.Contains(RunbookManifestValidator.Validate(m), e => e.Contains("'server'"));
    }

    [Fact]
    public void AgentRunbook_CannotTakeAdDn()
    {
        var m = AgentManifest();
        m.Parameters.Add(new RunbookParameter { Name = "UserDn", Type = RunbookParameterTypes.AdDn });
        Assert.Contains(RunbookManifestValidator.Validate(m), e => e.Contains("ad-dn"));
    }

    [Fact]
    public void ServerTarget_RequiresAgentExecutor()
    {
        var m = AgentManifest();
        m.Executor = RunbookExecutors.Server;
        Assert.Contains(RunbookManifestValidator.Validate(m), e => e.Contains("yalnızca agent"));
    }

    [Fact]
    public void CoreBinder_WithoutDnValidator_RejectsAdDn()
    {
        var m = new RunbookManifest { Parameters = { new RunbookParameter { Name = "UserDn", Type = RunbookParameterTypes.AdDn } } };
        var bound = RunbookParameterCore.Bind(m, new Dictionary<string, string?> { ["UserDn"] = "CN=x,DC=y" }, null);
        Assert.Contains(bound.Errors, e => e.Contains("desteklenmiyor"));
    }

    [Fact]
    public void Runner_RejectsUnsafeValues_EvenIfNotBound()
    {
        var bound = new BoundRunbookParameters(new Dictionary<string, string> { ["X"] = "-Command evil" }, Array.Empty<string>(), Array.Empty<string>());
        Assert.Throws<ArgumentException>(() => RunbookProcessRunner.BuildArguments("s.ps1", bound, false));
    }
}

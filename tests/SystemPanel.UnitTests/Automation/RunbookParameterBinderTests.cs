using SystemPanel.Automation;
using SystemPanel.Contracts.Automation;
using SystemPanel.Security.Scope;

namespace SystemPanel.UnitTests.Automation;

public sealed class RunbookParameterBinderTests
{
    private const string Ankara = "OU=ANKARA,OU=IL,DC=corp,DC=example";

    private static readonly OrganizationalScope AnkaraScope =
        OrganizationalScope.ForRoots(new[] { DistinguishedName.Parse(Ankara) });

    private static RunbookManifest Manifest() => new()
    {
        Parameters =
        {
            new RunbookParameter { Name = "UserDn", Type = RunbookParameterTypes.AdDn },
            new RunbookParameter { Name = "Days", Type = RunbookParameterTypes.Int, Min = 7, Max = 365, Default = "30" },
            new RunbookParameter { Name = "Mode", Type = RunbookParameterTypes.Enum, Values = { "Report", "Disable" }, Required = false },
            new RunbookParameter { Name = "Reason", Type = RunbookParameterTypes.String, MaxLength = 20, Pattern = "[A-Za-z ]+", Required = false },
            new RunbookParameter { Name = "Server", Type = RunbookParameterTypes.Hostname, Required = false },
            new RunbookParameter { Name = "Notify", Type = RunbookParameterTypes.Bool, Required = false },
        },
    };

    private static BoundRunbookParameters Bind(Dictionary<string, string?> input, OrganizationalScope? scope = null)
        => RunbookParameterBinder.Bind(Manifest(), input, scope ?? AnkaraScope);

    [Fact]
    public void ValidInput_IsNormalized()
    {
        var b = Bind(new() { ["UserDn"] = "cn=Ali,ou=ankara,ou=il,dc=corp,dc=example", ["Notify"] = "true", ["Server"] = "SRV-01.corp.example" });

        Assert.True(b.IsValid, string.Join(";", b.Errors));
        Assert.Equal("30", b.Values["Days"]);
        Assert.Contains("Notify", b.Switches);
        Assert.False(b.Values.ContainsKey("Notify"));
    }

    [Fact]
    public void DnOutsideScope_IsRejected()
    {
        var b = Bind(new() { ["UserDn"] = "CN=Veli,OU=IZMIR,OU=IL,DC=corp,DC=example" });
        Assert.Contains(b.Errors, e => e.Contains("kapsam"));
    }

    [Fact]
    public void LookalikeOu_IsRejected()
    {
        var b = Bind(new() { ["UserDn"] = "CN=Veli,OU=XANKARA,OU=IL,DC=corp,DC=example" });
        Assert.False(b.IsValid);
    }

    [Fact]
    public void GlobalScope_AllowsAnyDn()
        => Assert.True(Bind(new() { ["UserDn"] = "CN=Veli,OU=IZMIR,OU=IL,DC=corp,DC=example" }, OrganizationalScope.Global).IsValid);

    [Theory]
    [InlineData("Days", "3")]
    [InlineData("Days", "abc")]
    [InlineData("Mode", "Delete")]
    [InlineData("Reason", "drop; table")]
    [InlineData("Reason", "this reason is far too long for it")]
    [InlineData("Server", "bad host!")]
    [InlineData("Notify", "maybe")]
    public void InvalidValues_AreRejected(string name, string value)
    {
        var b = Bind(new() { ["UserDn"] = "CN=Ali," + Ankara, [name] = value });
        Assert.False(b.IsValid);
    }

    [Theory]
    [InlineData("-Command Remove-Item")]
    [InlineData("a\"b")]
    [InlineData("a\nb")]
    public void InjectionShapedValues_AreRejected(string value)
    {
        var b = Bind(new() { ["UserDn"] = "CN=Ali," + Ankara, ["Mode"] = value });
        Assert.Contains(b.Errors, e => e.Contains("geçersiz karakter"));
    }

    [Fact]
    public void MissingRequired_IsRejected()
        => Assert.Contains(Bind(new()).Errors, e => e.Contains("zorunlu"));

    [Fact]
    public void UnknownInputs_AreIgnored()
    {
        var b = Bind(new() { ["UserDn"] = "CN=Ali," + Ankara, ["Evil"] = "x" });
        Assert.True(b.IsValid);
        Assert.False(b.Values.ContainsKey("Evil"));
    }
}

public sealed class RunbookOutputSanitizerTests
{
    [Theory]
    [InlineData("password=Hunter2", "Hunter2")]
    [InlineData("\"newPassword\": \"Gizli123!\"", "Gizli123!")]
    [InlineData("api_key: abc-123", "abc-123")]
    [InlineData("Parola=Deneme1", "Deneme1")]
    public void PlainText_SecretsAreMasked(string text, string secret)
        => Assert.DoesNotContain(secret, SystemPanel.Automation.RunbookOutputSanitizer.Sanitize(text, 4096));

    [Fact]
    public void Json_SensitiveFieldsAreMasked_OthersKept()
    {
        var output = SystemPanel.Automation.RunbookOutputSanitizer.Sanitize("{\"user\":\"ali\",\"lapsPassword\":\"x1\",\"items\":[{\"secret\":\"y2\",\"name\":\"n\"}]}", 4096);
        Assert.DoesNotContain("x1", output);
        Assert.DoesNotContain("y2", output);
        Assert.Contains("ali", output);
        Assert.Contains("\"name\"", output);
    }

    [Fact]
    public void LongOutput_IsTruncated()
    {
        var output = SystemPanel.Automation.RunbookOutputSanitizer.Sanitize(new string('ş', 10_000), 1024);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(output) <= 1024);
        Assert.Contains("kısaltıldı", output);
    }
}

public sealed class RunbookScriptHostArgumentsTests
{
    [Fact]
    public void Arguments_UseFileAndSeparateValues()
    {
        var bound = new SystemPanel.Contracts.Automation.BoundRunbookParameters(
            new Dictionary<string, string> { ["UserDn"] = "CN=Ali; Remove-Item C:\\,DC=x" },
            new[] { "Notify" },
            Array.Empty<string>());

        var args = SystemPanel.Automation.PowerShellRunbookScriptHost.BuildArguments("C:\\w\\s.ps1", bound, dryRun: true);

        Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "C:\\w\\s.ps1",
            "-UserDn", "CN=Ali; Remove-Item C:\\,DC=x", "-Notify", "-DryRun" }, args);
        Assert.DoesNotContain("-Command", args);
    }
}

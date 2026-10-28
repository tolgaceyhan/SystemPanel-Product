using System.Security.Claims;
using System.Text.Json;
using NSubstitute;
using SystemPanel.Auditing;
using SystemPanel.Configuration;
using SystemPanel.Models;
using SystemPanel.Security.Scope;
using SystemPanel.Services;
using SystemPanel.Services.ActiveDirectory;

namespace SystemPanel.UnitTests.ActiveDirectory;

public class LapsAndLoginTests
{
    [Fact]
    public void Windows_laps_json_is_parsed_and_preferred()
    {
        var r = LapsResult.FromAttributes("{\"n\":\"Administrator\",\"t\":\"1d8161b41c41cde\",\"p\":\"W1nLaps!\"}", 133500000000000000, false, "legacy", null);

        Assert.Equal(LapsSource.WindowsLaps, r.Source);
        Assert.Equal("W1nLaps!", r.Password);
        Assert.Equal("Administrator", r.AccountName);
        Assert.NotNull(r.ExpiresUtc);
        Assert.NotNull(r.UpdatedUtc);
    }

    [Fact]
    public void Encrypted_windows_laps_is_reported_not_faked()
    {
        var r = LapsResult.FromAttributes(null, null, hasEncryptedWindowsLaps: true, legacyPassword: null, legacyExpiration: null);

        Assert.Equal(LapsSource.WindowsLapsEncrypted, r.Source);
        Assert.False(r.HasPassword);
        Assert.Contains("Get-LapsADPassword", r.Message);
    }

    [Fact]
    public void Legacy_laps_is_used_as_fallback()
    {
        var r = LapsResult.FromAttributes(null, null, false, "L3gacy!", 133500000000000000);
        Assert.Equal(LapsSource.LegacyLaps, r.Source);
        Assert.Equal("L3gacy!", r.Password);
    }

    [Fact]
    public void Laps_result_never_prints_the_password()
    {
        var r = LapsResult.FromAttributes(null, null, false, "S3cr3t!", null);
        Assert.DoesNotContain("S3cr3t!", r.ToString());
    }

    [Fact]
    public async Task Laps_reveal_outside_scope_is_denied_and_audited_without_password()
    {
        var (ops, ad, audits) = Build(OrganizationalScope.ForRoots(new[] { DistinguishedName.Parse("OU=ANKARA,DC=corp") }));
        ad.GetComputerByNameAsync("PC1", Arg.Any<CancellationToken>()).Returns(new AdComputer { Name = "PC1", DistinguishedName = "CN=PC1,OU=IZMIR,DC=corp" });

        var (result, laps) = await ops.RevealLapsAsync(new ClaimsPrincipal(), "PC1");

        Assert.Equal(403, result.StatusCode);
        Assert.Null(laps);
        await ad.DidNotReceiveWithAnyArgs().ReadLapsAsync(default!, default);
        Assert.Equal(AuditActions.LapsRead, Assert.Single(audits).Action);
    }

    [Fact]
    public async Task Laps_reveal_inside_scope_returns_password_but_audit_does_not_contain_it()
    {
        var (ops, ad, audits) = Build(OrganizationalScope.ForRoots(new[] { DistinguishedName.Parse("OU=ANKARA,DC=corp") }));
        ad.GetComputerByNameAsync("PC1", Arg.Any<CancellationToken>()).Returns(new AdComputer { Name = "PC1", DistinguishedName = "CN=PC1,OU=ANKARA,DC=corp" });
        ad.ReadLapsAsync("CN=PC1,OU=ANKARA,DC=corp", Arg.Any<CancellationToken>()).Returns(LapsResult.FromAttributes(null, null, false, "Visible0nce!", null));

        var (result, laps) = await ops.RevealLapsAsync(new ClaimsPrincipal(), "PC1");

        Assert.True(result.Success);
        Assert.Equal("Visible0nce!", laps!.Password);
        var audit = Assert.Single(audits);
        Assert.True(audit.Success);
        Assert.DoesNotContain("Visible0nce!", JsonSerializer.Serialize(audit));
        Assert.DoesNotContain("Visible0nce!", AuditRedactor.ToRedactedJson(audit.Metadata));
    }

    private static (AdComputerOperations, IAdComputerService, List<AuditRecord>) Build(OrganizationalScope scope)
    {
        var ad = Substitute.For<IAdComputerService>();
        var scopes = Substitute.For<IOrganizationalScopeResolver>();
        scopes.ResolveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(scope);
        var audits = new List<AuditRecord>();
        var audit = Substitute.For<IAuditService>();
        audit.RecordAsync(Arg.Do<AuditRecord>(audits.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return (new AdComputerOperations(ad, scopes, audit), ad, audits);
    }

    [Fact]
    public void Roles_are_mapped_from_group_names_or_dns_case_insensitively()
    {
        var map = new RoleMappingOptions
        {
            ["Admin"] = new[] { "Domain Admins", "IT-SuperAdmins" },
            ["LocalAdmin"] = new[] { "ANK_Local_Admins" },
            ["Helpdesk"] = new[] { "HelpdeskTeam" },
        };

        var roles = LoginService.MapRoles(new[] { "CN=ank_local_admins,OU=Gruplar,DC=corp", "helpdeskteam", "Unrelated" }, map);

        Assert.Equal(new[] { "Helpdesk", "LocalAdmin" }, roles.OrderBy(r => r));
        Assert.Empty(LoginService.MapRoles(new[] { "Everyone" }, map));
    }

    [Theory]
    [InlineData("ali", "corp.example", "ali", "ali@corp.example")]
    [InlineData("CORP\\ali", "corp.example", "ali", "ali@corp.example")]
    [InlineData("ali@corp.example", "corp.example", "ali", "ali@corp.example")]
    public void User_input_is_split_into_sam_and_upn(string input, string domain, string sam, string upn)
    {
        Assert.Equal((sam, upn), LoginService.SplitUser(input, domain));
    }

    [Theory]
    [InlineData("a*b", "a\\2ab")]
    [InlineData("(x)", "\\28x\\29")]
    [InlineData("c\\d", "c\\5cd")]
    [InlineData("*)(uid=*", "\\2a\\29\\28uid=\\2a")]
    public void Ldap_filter_values_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, LdapFilter.Escape(input));
    }
}

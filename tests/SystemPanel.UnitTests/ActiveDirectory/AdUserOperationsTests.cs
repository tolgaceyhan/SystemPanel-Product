using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SystemPanel.Auditing;
using SystemPanel.Models;
using SystemPanel.Security.Scope;
using SystemPanel.Services;
using SystemPanel.Services.ActiveDirectory;
using SystemPanel.ViewModels.AdUser;

namespace SystemPanel.UnitTests.ActiveDirectory;

public class AdUserOperationsTests
{
    private const string Ankara = "OU=ANKARA,OU=IL,DC=corp,DC=example";
    private const string Izmir = "OU=IZMIR,OU=IL,DC=corp,DC=example";
    private const string AnkaraUser = "CN=Ayse,OU=KULLANICILAR," + Ankara;
    private const string IzmirUser = "CN=Mehmet,OU=KULLANICILAR," + Izmir;

    private readonly IAdUserService _ad = Substitute.For<IAdUserService>();
    private readonly IOrganizationalScopeResolver _scopes = Substitute.For<IOrganizationalScopeResolver>();
    private readonly List<AuditRecord> _audits = new();
    private readonly AdUserOperations _ops;
    private readonly ClaimsPrincipal _actor = new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "ankara.admin"), new Claim(ScopeClaimTypes.AdDn, "CN=Self,OU=KULLANICILAR," + Ankara) }, "test"));

    public AdUserOperationsTests()
    {
        var audit = Substitute.For<IAuditService>();
        audit.RecordAsync(Arg.Do<AuditRecord>(_audits.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _ops = new AdUserOperations(_ad, _scopes, audit, NullLogger<AdUserOperations>.Instance);

        UseScope(OrganizationalScope.ForRoots(new[] { DistinguishedName.Parse(Ankara) }));
        Register(AnkaraUser);
        Register(IzmirUser);
        Register("CN=Self,OU=KULLANICILAR," + Ankara);
        _ad.ResetPasswordManualAsync(default!, default!).ReturnsForAnyArgs(true);
        _ad.ResetPasswordAndUnlockAsync(default!, default!).ReturnsForAnyArgs(true);
        _ad.DisableUserAsync(default!).ReturnsForAnyArgs(true);
        _ad.MoveUserToOuAsync(default!, default!).ReturnsForAnyArgs(true);
        _ad.AddUserToGroupByDn(default!, default!).ReturnsForAnyArgs(true);
        _ad.BuildOuDn(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
    }

    private void UseScope(OrganizationalScope scope)
        => _scopes.ResolveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(scope);

    private AdUser Register(string dn, bool privileged = false)
    {
        var u = new AdUser { DistinguishedName = dn, Id = dn, SamAccountName = dn.Split(',')[0][3..], IsPrivileged = privileged };
        _ad.GetUserByIdAsync(dn).Returns(u);
        return u;
    }

    [Fact]
    public async Task Local_admin_cannot_reset_password_in_another_province()
    {
        var result = await _ops.ResetPasswordAsync(_actor, IzmirUser, "N3w-Passw0rd!", unlock: false, mustChangeAtNextLogon: false);

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        await _ad.DidNotReceiveWithAnyArgs().ResetPasswordManualAsync(default!, default!);

        var audit = Assert.Single(_audits);
        Assert.Equal(AuditActions.AdUserPasswordReset, audit.Action);
        Assert.False(audit.Success);
        Assert.Equal("denied", audit.ErrorCode);
        Assert.Equal(IzmirUser, audit.ResourceId);
    }

    [Fact]
    public async Task Password_reset_inside_scope_succeeds_and_password_is_never_audited()
    {
        const string password = "Gizli-Parola-123";
        var result = await _ops.ResetPasswordAsync(_actor, AnkaraUser, password, unlock: true, mustChangeAtNextLogon: false);

        Assert.True(result.Success);
        await _ad.Received(1).ResetPasswordAndUnlockAsync(AnkaraUser, password);

        var audit = Assert.Single(_audits);
        Assert.True(audit.Success);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(audit));
    }

    [Fact]
    public async Task Spoofed_dn_with_escaped_comma_is_resolved_and_rejected()
    {
        // İstemci kendi DN dizesini gönderse bile kapsam, AD'den okunan kanonik DN ile kontrol edilir.
        const string spoof = @"CN=evil\,OU=ANKARA,OU=IL,DC=corp,DC=example";
        Register(spoof);

        var result = await _ops.SetEnabledAsync(_actor, spoof, enabled: false);

        Assert.Equal(403, result.StatusCode);
        await _ad.DidNotReceiveWithAnyArgs().DisableUserAsync(default!);
    }

    [Fact]
    public async Task Privileged_account_requires_global_scope()
    {
        const string admin = "CN=DomainAdminInAnkara,OU=KULLANICILAR," + Ankara;
        Register(admin, privileged: true);

        var denied = await _ops.ResetPasswordAsync(_actor, admin, "N3w-Passw0rd!", false, false);
        Assert.Equal(403, denied.StatusCode);

        UseScope(OrganizationalScope.Global);
        var allowed = await _ops.ResetPasswordAsync(_actor, admin, "N3w-Passw0rd!", false, false);
        Assert.True(allowed.Success);
    }

    [Fact]
    public async Task Unknown_target_returns_not_found_without_touching_ad()
    {
        var result = await _ops.UnlockAsync(_actor, "CN=Ghost,OU=X,DC=corp,DC=example");

        Assert.Equal(404, result.StatusCode);
        await _ad.DidNotReceiveWithAnyArgs().UnlockAccountAsync(default!);
    }

    [Fact]
    public async Task Move_requires_target_ou_inside_scope()
    {
        var result = await _ops.MoveAsync(_actor, AnkaraUser, "OU=KULLANICILAR," + Izmir);

        Assert.Equal(403, result.StatusCode);
        await _ad.DidNotReceiveWithAnyArgs().MoveUserToOuAsync(default!, default!);
    }

    [Fact]
    public async Task Move_cannot_pull_a_user_from_another_province()
    {
        // Eski açık: kaynak kullanıcı kontrol edilmiyordu, yalnızca hedef OU.
        var result = await _ops.MoveAsync(_actor, IzmirUser, "OU=KULLANICILAR," + Ankara);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Move_within_scope_succeeds_and_records_before_after()
    {
        var result = await _ops.MoveAsync(_actor, AnkaraUser, "OU=PASIF," + Ankara);

        Assert.True(result.Success);
        var audit = Assert.Single(_audits);
        Assert.Contains("OU=KULLANICILAR", JsonSerializer.Serialize(audit.Before));
        Assert.Contains("OU=PASIF", JsonSerializer.Serialize(audit.After));
    }

    [Fact]
    public async Task Protected_groups_cannot_be_modified()
    {
        const string group = "CN=Domain Admins,CN=Users,DC=corp,DC=example";
        _ad.IsProtectedGroup(group).Returns(true);
        UseScope(OrganizationalScope.Global);

        var result = await _ops.AddToGroupAsync(_actor, AnkaraUser, group);

        Assert.Equal(403, result.StatusCode);
        await _ad.DidNotReceiveWithAnyArgs().AddUserToGroupByDn(default!, default!);
    }

    [Fact]
    public async Task Groups_outside_scope_cannot_be_assigned()
    {
        var result = await _ops.AddToGroupAsync(_actor, AnkaraUser, "CN=IZMIR_Printers," + Izmir);
        Assert.Equal(403, result.StatusCode);

        var ok = await _ops.AddToGroupAsync(_actor, AnkaraUser, "CN=ANKARA_Printers," + Ankara);
        Assert.True(ok.Success);
    }

    [Fact]
    public async Task User_cannot_disable_own_account()
    {
        var result = await _ops.SetEnabledAsync(_actor, "CN=Self,OU=KULLANICILAR," + Ankara, enabled: false);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Search_for_local_admin_only_queries_scope_roots_and_filters_results()
    {
        _ad.GetAdUsersInOuAsync("ali", Ankara, Arg.Any<CancellationToken>()).Returns(new List<AdUser>
        {
            new() { DistinguishedName = AnkaraUser },
            new() { DistinguishedName = IzmirUser }, // hatalı bir dönüş bile süzülür
        });

        var result = await _ops.SearchAsync(_actor, "ali", null);

        Assert.Single(result);
        Assert.Equal(AnkaraUser, result[0].DistinguishedName);
        await _ad.DidNotReceive().GetAdUsersInOuAsync(Arg.Any<string>(), Izmir, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_with_out_of_scope_ou_returns_nothing()
    {
        var result = await _ops.SearchAsync(_actor, "", Izmir);

        Assert.Empty(result);
        await _ad.DidNotReceiveWithAnyArgs().GetAdUsersInOuAsync(default!, default!, default);
    }

    [Fact]
    public async Task Global_search_without_term_does_not_dump_the_domain()
    {
        UseScope(OrganizationalScope.Global);
        Assert.Empty(await _ops.SearchAsync(_actor, "", null));
    }

    [Fact]
    public async Task Empty_scope_sees_nothing()
    {
        UseScope(OrganizationalScope.None);
        Assert.Empty(await _ops.SearchAsync(_actor, "ali", null));
        Assert.Null(await _ops.GetUserAsync(_actor, AnkaraUser));
        Assert.Equal(403, (await _ops.UnlockAsync(_actor, AnkaraUser)).StatusCode);
    }

    [Fact]
    public async Task Create_requires_target_ou_in_scope()
    {
        var vm = new CreateAdUserViewModel { UserName = "yeni", DisplayName = "Yeni", Email = "y@x", Password = "P@ssw0rd123", SelectedOu = "OU=KULLANICILAR," + Izmir };

        var result = await _ops.CreateAsync(_actor, vm);

        Assert.Equal(403, result.StatusCode);
        await _ad.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!);
        Assert.DoesNotContain("P@ssw0rd123", JsonSerializer.Serialize(_audits));
    }

    [Fact]
    public void Ou_tree_is_pruned_to_scope_roots()
    {
        var tree = new OuTreeDto
        {
            OuName = "corp", DistinguishedName = "DC=corp,DC=example",
            Children =
            {
                new OuTreeDto { OuName = "IL", DistinguishedName = "OU=IL,DC=corp,DC=example", Children =
                {
                    new OuTreeDto { OuName = "ANKARA", DistinguishedName = Ankara, Children = { new OuTreeDto { OuName = "KULLANICILAR", DistinguishedName = "OU=KULLANICILAR," + Ankara } } },
                    new OuTreeDto { OuName = "IZMIR", DistinguishedName = Izmir },
                } },
            },
        };

        var pruned = AdUserOperations.PruneToScope(tree, OrganizationalScope.ForRoots(new[] { DistinguishedName.Parse(Ankara) }));

        var node = Assert.Single(pruned.Children);
        Assert.Equal(Ankara, node.DistinguishedName);
        Assert.Single(node.Children);
    }
}

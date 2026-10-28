using System.Security.Claims;
using NSubstitute;
using SystemPanel.Security;
using SystemPanel.Security.Scope;

namespace SystemPanel.UnitTests.Security;

public class OrganizationalScopeTests
{
    private const string Ankara = "OU=ANKARA,OU=IL,DC=corp,DC=example";
    private const string Izmir = "OU=IZMIR,OU=IL,DC=corp,DC=example";

    private static ClaimsPrincipal User(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "test"));

    private static IOrganizationalScopeResolver Resolver(bool hasScopeAll)
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Permissions.ScopeAll, Arg.Any<CancellationToken>())
            .Returns(hasScopeAll);
        return new OrganizationalScopeResolver(permissions);
    }

    [Fact]
    public async Task Scope_all_permission_gives_global_scope()
    {
        var scope = await Resolver(hasScopeAll: true).ResolveAsync(User(new Claim(ClaimTypes.Name, "admin")));

        Assert.True(scope.IsGlobal);
        Assert.True(scope.Contains("CN=Domain Admins,CN=Users,DC=corp,DC=example"));
    }

    [Fact]
    public async Task Local_admin_is_limited_to_own_province()
    {
        var scope = await Resolver(false).ResolveAsync(User(new Claim(ScopeClaimTypes.OuRoot, Ankara)));

        Assert.False(scope.IsGlobal);
        Assert.True(scope.Contains("CN=user1,OU=KULLANICILAR," + Ankara));
        Assert.False(scope.Contains("CN=user2,OU=KULLANICILAR," + Izmir));
        Assert.False(scope.Contains("CN=Domain Admins,CN=Users,DC=corp,DC=example"));
        Assert.False(scope.Contains("OU=IL,DC=corp,DC=example"));
    }

    [Fact]
    public async Task Multiple_roots_are_combined()
    {
        var scope = await Resolver(false).ResolveAsync(User(
            new Claim(ScopeClaimTypes.OuRoot, Ankara),
            new Claim(ScopeClaimTypes.OuRoot, Izmir)));

        Assert.True(scope.Contains("CN=a," + Ankara));
        Assert.True(scope.Contains("CN=b," + Izmir));
        Assert.Equal(2, scope.Roots.Count);
    }

    [Fact]
    public async Task No_scope_claim_means_no_access_fail_closed()
    {
        var scope = await Resolver(false).ResolveAsync(User(new Claim(ClaimTypes.Name, "localadmin")));

        Assert.True(scope.IsEmpty);
        Assert.False(scope.Contains("CN=a," + Ankara));
    }

    [Fact]
    public async Task Unparseable_scope_claim_is_ignored_not_treated_as_global()
    {
        var scope = await Resolver(false).ResolveAsync(User(new Claim(ScopeClaimTypes.OuRoot, "garbage")));

        Assert.True(scope.IsEmpty);
    }

    [Fact]
    public async Task Anonymous_user_has_no_scope()
    {
        var scope = await Resolver(true).ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True(scope.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-dn")]
    public void Unparseable_target_is_out_of_scope(string? target)
    {
        var scope = OrganizationalScope.ForRoots(new[] { DistinguishedName.Parse(Ankara) });
        Assert.False(scope.Contains(target));
    }
}

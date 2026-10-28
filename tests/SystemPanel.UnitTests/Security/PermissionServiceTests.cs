using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NSubstitute;
using SystemPanel.Security;

namespace SystemPanel.UnitTests.Security;

public class PermissionServiceTests
{
    private static ClaimsPrincipal UserWithRoles(params string[] roles)
        => new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    private static PermissionService ServiceWith(IReadOnlyDictionary<string, IReadOnlyCollection<string>> map)
    {
        var store = Substitute.For<IRolePermissionStore>();
        store.GetMapAsync(Arg.Any<CancellationToken>()).Returns(map);
        return new PermissionService(store);
    }

    [Fact]
    public async Task Permissions_are_the_union_of_all_roles()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        var perms = await svc.GetPermissionsAsync(UserWithRoles("Helpdesk", "Monitoring"));

        Assert.Contains(Permissions.AdUsersCreate, perms);
        Assert.Contains(Permissions.MonitoringView, perms);
        Assert.DoesNotContain(Permissions.ScopeAll, perms);
    }

    [Fact]
    public async Task Role_names_are_case_insensitive()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        Assert.True(await svc.HasPermissionAsync(UserWithRoles("localadmin"), Permissions.LapsRead));
    }

    [Fact]
    public async Task Unknown_role_grants_nothing()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        Assert.Empty(await svc.GetPermissionsAsync(UserWithRoles("User")));
    }

    [Fact]
    public async Task Anonymous_user_has_no_permissions()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        var anon = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") })); // kimliği doğrulanmamış

        Assert.Empty(await svc.GetPermissionsAsync(anon));
    }

    [Fact]
    public async Task Local_admin_cannot_act_organization_wide_by_default()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        var perms = await svc.GetPermissionsAsync(UserWithRoles("LocalAdmin"));

        Assert.DoesNotContain(Permissions.ScopeAll, perms);
        Assert.DoesNotContain(Permissions.GpoDelete, perms);
        Assert.DoesNotContain(Permissions.UsersManage, perms);
        Assert.DoesNotContain(Permissions.SmsBulkSend, perms);
    }

    [Fact]
    public async Task Admin_has_every_permission()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        var perms = await svc.GetPermissionsAsync(UserWithRoles("Admin"));

        Assert.All(Permissions.All, p => Assert.Contains(p, perms));
    }

    [Fact]
    public void Default_map_only_uses_known_permissions()
    {
        foreach (var (role, perms) in DefaultRolePermissions.Map)
            Assert.All(perms, p => Assert.True(Permissions.IsKnown(p), $"{role}: {p}"));
    }

    [Fact]
    public void Every_permission_has_a_description()
    {
        Assert.All(Permissions.All, p => Assert.True(Permissions.Descriptions.ContainsKey(p), p));
    }

    [Fact]
    public void Permission_names_follow_dotted_lowercase_convention()
    {
        Assert.All(Permissions.All, p => Assert.Matches("^[a-z]+(\\.[a-z]+)+$", p));
    }

    [Fact]
    public void RequirePermission_rejects_unknown_permissions_at_startup()
    {
        Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute("ad.users.everything"));
    }

    [Fact]
    public async Task Policy_provider_builds_permission_policies()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync(new RequirePermissionAttribute(Permissions.LapsRead).Policy!);

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(Permissions.LapsRead, requirement.Permission);
    }

    [Fact]
    public async Task Handler_succeeds_only_with_permission()
    {
        var svc = ServiceWith(DefaultRolePermissions.Map);
        var handler = new PermissionAuthorizationHandler(svc);
        var requirement = new PermissionRequirement(Permissions.GpoDelete);

        var allowed = new AuthorizationHandlerContext(new[] { requirement }, UserWithRoles("Admin"), null);
        await handler.HandleAsync(allowed);
        Assert.True(allowed.HasSucceeded);

        var denied = new AuthorizationHandlerContext(new[] { requirement }, UserWithRoles("LocalAdmin"), null);
        await handler.HandleAsync(denied);
        Assert.False(denied.HasSucceeded);
    }
}

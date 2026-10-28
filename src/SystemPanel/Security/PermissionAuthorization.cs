using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SystemPanel.Security
{
    /// <summary>
    /// Bir aksiyonu izin ile korur: <c>[RequirePermission(Permissions.AdUsersView)]</c>.
    /// Birden fazla attribute verilirse hepsi gerekir (AND).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RequirePermissionAttribute : AuthorizeAttribute
    {
        public const string PolicyPrefix = "perm:";

        public RequirePermissionAttribute(string permission)
        {
            if (!Permissions.IsKnown(permission))
                throw new ArgumentException($"Bilinmeyen izin: {permission}", nameof(permission));

            Permission = permission;
            Policy = PolicyPrefix + permission;
        }

        public string Permission { get; }
    }

    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission) => Permission = permission;
        public string Permission { get; }
    }

    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissions;

        public PermissionAuthorizationHandler(IPermissionService permissions) => _permissions = permissions;

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (await _permissions.HasPermissionAsync(context.User, requirement.Permission))
                context.Succeed(requirement);
        }
    }

    /// <summary>"perm:" önekli politikaları dinamik olarak üretir; diğerlerini varsayılan sağlayıcıya bırakır.</summary>
    public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
            => _fallback = new DefaultAuthorizationPolicyProvider(options);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            {
                var permission = policyName.Substring(RequirePermissionAttribute.PolicyPrefix.Length);
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            return _fallback.GetPolicyAsync(policyName);
        }
    }

    public static class PermissionPrincipalExtensions
    {
        /// <summary>Razor görünümleri için: menü / buton görünürlüğü. Yetki kontrolünün yerine GEÇMEZ.</summary>
        public static async Task<bool> HasAnyPermissionAsync(this IPermissionService service, System.Security.Claims.ClaimsPrincipal user, params string[] anyOf)
        {
            var set = await service.GetPermissionsAsync(user);
            return anyOf.Any(set.Contains);
        }
    }
}

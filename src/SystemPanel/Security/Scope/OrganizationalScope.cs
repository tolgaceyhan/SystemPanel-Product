using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace SystemPanel.Security.Scope
{
    /// <summary>
    /// Bir kullanıcının işlem yapabileceği organizasyonel alan (il / birim OU'ları).
    /// Kapsam yalnızca UI'da değil, servis katmanında (ör. <c>AdUserOperations</c>)
    /// her hedef nesne için kontrol edilir. Varsayılan "hiçbir şey"dir (fail-closed).
    /// </summary>
    public sealed class OrganizationalScope
    {
        public static readonly OrganizationalScope Global = new(isGlobal: true, Array.Empty<DistinguishedName>());
        public static readonly OrganizationalScope None = new(isGlobal: false, Array.Empty<DistinguishedName>());

        private OrganizationalScope(bool isGlobal, IReadOnlyList<DistinguishedName> roots)
        {
            IsGlobal = isGlobal;
            Roots = roots;
        }

        public bool IsGlobal { get; }

        /// <summary>Kullanıcının yetkili olduğu OU kökleri (global değilse).</summary>
        public IReadOnlyList<DistinguishedName> Roots { get; }

        public bool IsEmpty => !IsGlobal && Roots.Count == 0;

        public static OrganizationalScope ForRoots(IEnumerable<DistinguishedName> roots)
        {
            var distinct = roots.Distinct().ToArray();
            return distinct.Length == 0 ? None : new OrganizationalScope(false, distinct);
        }

        public bool Contains(DistinguishedName target)
            => IsGlobal || Roots.Any(target.IsWithin);

        /// <summary>Ayrıştırılamayan DN'ler kapsam DIŞINDA sayılır.</summary>
        public bool Contains(string? targetDn)
            => IsGlobal || (DistinguishedName.TryParse(targetDn, out var dn) && Contains(dn));

        /// <summary>Denetim kaydı için kısa gösterim.</summary>
        public string Describe()
            => IsGlobal ? "*" : IsEmpty ? "(none)" : string.Join(" | ", Roots.Select(r => r.ToString()));
    }

    public interface IOrganizationalScopeResolver
    {
        Task<OrganizationalScope> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default);
    }

    /// <summary>
    /// Kapsamı belirler:
    /// <list type="bullet">
    /// <item><see cref="Permissions.ScopeAll"/> izni → global.</item>
    /// <item>Aksi halde oturum açılırken AD'den türetilen <see cref="ScopeClaimTypes.OuRoot"/> claim'leri.</item>
    /// <item>Hiçbiri yoksa → <see cref="OrganizationalScope.None"/> (hiçbir hedefe erişemez).</item>
    /// </list>
    /// </summary>
    public sealed class OrganizationalScopeResolver : IOrganizationalScopeResolver
    {
        private readonly IPermissionService _permissions;

        public OrganizationalScopeResolver(IPermissionService permissions) => _permissions = permissions;

        public async Task<OrganizationalScope> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return OrganizationalScope.None;

            if (await _permissions.HasPermissionAsync(user, Permissions.ScopeAll, ct))
                return OrganizationalScope.Global;

            var roots = user.FindAll(ScopeClaimTypes.OuRoot)
                .Select(c => DistinguishedName.TryParse(c.Value, out var dn) ? dn : null)
                .Where(dn => dn != null)
                .Cast<DistinguishedName>();

            return OrganizationalScope.ForRoots(roots);
        }
    }

    public static class ScopeClaimTypes
    {
        /// <summary>Kullanıcının yetkili olduğu kök OU DN'i (birden fazla olabilir).</summary>
        public const string OuRoot = "ou:root";

        /// <summary>LocalAdmin gruplarından türetilen il adı (SCCM boundary eşlemesi için).</summary>
        public const string Province = "sp:province";

        /// <summary>Kullanıcının kendi AD DN'i.</summary>
        public const string AdDn = "ad:dn";

        /// <summary>Kullanıcının sAMAccountName değeri.</summary>
        public const string AdSam = "ad:sam";
    }

    /// <summary>Hedef nesne kullanıcının kapsamı dışında.</summary>
    public sealed class ScopeViolationException : Exception
    {
        public ScopeViolationException(string message) : base(message) { }
    }
}

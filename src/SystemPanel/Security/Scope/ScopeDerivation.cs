using System;
using System.Collections.Generic;
using System.Linq;
using SystemPanel.Configuration;

namespace SystemPanel.Security.Scope
{
    /// <summary>
    /// Kullanıcının kendi AD DN'inden il kapsam köklerini (ou:root) türetir. Saf fonksiyon; birim testli.
    /// Bkz. <see cref="ScopeDerivationOptions"/>.
    /// </summary>
    public static class ScopeDerivation
    {
        public static IReadOnlyList<DistinguishedName> DeriveRoots(string? userDn, ScopeDerivationOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.AnchorOu) || !DistinguishedName.TryParse(userDn, out var dn))
                return Array.Empty<DistinguishedName>();

            var rdns = dn.Rdns;
            int anchor = -1;
            for (int i = 0; i < rdns.Count; i++)
            {
                if (rdns[i].Type == "OU" && string.Equals(rdns[i].Value, options.AnchorOu, StringComparison.OrdinalIgnoreCase))
                {
                    anchor = i;
                    break;
                }
            }

            // Anchor yok ya da kullanıcı doğrudan anchor altında (il yok) → kapsam yok.
            if (anchor <= 0)
                return Array.Empty<DistinguishedName>();

            bool IsBranch(DistinguishedName.Rdn r)
                => r.Type == "OU" && options.BranchOus.Any(b => string.Equals(b, r.Value, StringComparison.OrdinalIgnoreCase));

            int provinceIndex = anchor - 1;
            if (IsBranch(rdns[provinceIndex]))
                provinceIndex--;

            if (provinceIndex < 0 || rdns[provinceIndex].Type != "OU" || IsBranch(rdns[provinceIndex]))
                return Array.Empty<DistinguishedName>();

            var province = rdns[provinceIndex].ToString();
            var anchorDn = string.Join(",", rdns.Skip(anchor).Select(r => r.ToString()));

            var roots = new List<string> { $"{province},{anchorDn}" };
            foreach (var branch in options.BranchOus.Where(b => !string.IsNullOrWhiteSpace(b)))
                roots.Add($"{province},OU={DistinguishedName.Rdn.Escape(branch.Trim())},{anchorDn}");

            return roots
                .Select(r => DistinguishedName.TryParse(r, out var parsed) ? parsed : null)
                .Where(r => r != null)
                .Cast<DistinguishedName>()
                .Distinct()
                .ToList();
        }

        /// <summary>İl kapsamı için okunabilir ad (ör. "ANKARA"); türetilemezse null.</summary>
        public static string? ProvinceName(IReadOnlyList<DistinguishedName> roots)
            => roots.Count == 0 ? null : roots[0].Rdns[0].Value;
    }
}

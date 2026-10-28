using System;
using System.Linq;
using System.Text.RegularExpressions;
using SystemPanel.Security.Scope;

namespace SystemPanel.Services.Gpo
{
    /// <summary>GPO işlemlerine giden kullanıcı girdilerinin doğrulanması (allowlist).</summary>
    public static class GpoInput
    {
        private static readonly Regex DisplayName = new(@"^[\p{L}\p{N}][\p{L}\p{N} _.,()\[\]&'+\-]{0,199}$", RegexOptions.Compiled);

        public static bool TryNormalizeGuid(string? value, out string guid)
        {
            guid = string.Empty;
            if (!Guid.TryParse(value?.Trim().Trim('{', '}'), out var g))
                return false;
            guid = g.ToString("D");
            return true;
        }

        public static bool IsValidDisplayName(string? value)
            => !string.IsNullOrWhiteSpace(value) && DisplayName.IsMatch(value.Trim());

        /// <summary>
        /// OU DN'i geçerliyse ve (yapılandırılmışsa) domain kökünün altındaysa kanonik halini döndürür.
        /// </summary>
        public static bool TryNormalizeOuDn(string? value, string? baseDn, out string ouDn)
        {
            ouDn = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(c => c == '"' || char.IsControl(c)))
                return false;

            if (!DistinguishedName.TryParse(value, out var dn))
                return false;

            // GPO yalnızca OU, domain kökü (DC=) veya site'a bağlanır; CN= (kullanıcı/grup) hedef olamaz.
            var leafType = dn.Rdns[0].Type;
            if (leafType != "OU" && leafType != "DC")
                return false;

            if (!string.IsNullOrWhiteSpace(baseDn) && DistinguishedName.TryParse(baseDn, out var root) && !dn.IsWithin(root))
                return false;

            ouDn = dn.ToString();
            return true;
        }
    }
}

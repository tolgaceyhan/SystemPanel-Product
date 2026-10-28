using System.Collections.Generic;
using System.Linq;
using SystemPanel.Contracts.Automation;
using SystemPanel.Security.Scope;

namespace SystemPanel.Automation
{
    /// <summary>
    /// Sunucu tarafı parametre doğrulaması: ortak kurallar (<see cref="RunbookParameterCore"/>) + ad-dn için
    /// RFC 4514 ayrıştırma ve organizasyonel kapsam kontrolü.
    /// </summary>
    public static class RunbookParameterBinder
    {
        public static BoundRunbookParameters Bind(RunbookManifest manifest, IReadOnlyDictionary<string, string?> input, OrganizationalScope scope)
            => RunbookParameterCore.Bind(manifest, input, (p, value) =>
            {
                if (value.Length > 1024 || !DistinguishedName.TryParse(value, out var dn))
                    return (null, "geçerli bir AD ayırt edici adı (DN) olmalı.");
                if (p.InScope && !scope.Contains(dn))
                    return (null, "yetki kapsamınızın dışında.");
                return (dn.ToString(), null);
            });

        /// <summary>Hedef özeti (liste ekranı için): ad-dn ve sunucu adı parametrelerinin değerleri.</summary>
        public static string? Summarize(RunbookManifest manifest, BoundRunbookParameters bound)
        {
            var parts = manifest.Parameters
                .Where(p => p.Type is RunbookParameterTypes.AdDn or RunbookParameterTypes.Hostname && bound.Values.ContainsKey(p.Name))
                .Select(p => bound.Values[p.Name])
                .ToList();
            var text = string.Join("; ", parts);
            return text.Length == 0 ? null : text.Length > 500 ? text[..500] : text;
        }
    }
}

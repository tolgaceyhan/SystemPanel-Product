using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SystemPanel.Contracts.Automation;
using SystemPanel.Security.Scope;

namespace SystemPanel.Automation
{
    public sealed record BoundRunbookParameters(
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyList<string> Switches,
        IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Kullanıcı girdisini manifestteki parametre şemasına göre doğrular ve normalleştirir.
    /// Değerler betiğe yalnızca <c>-File</c> + ayrı argüman olarak geçer (kod olarak yorumlanmaz);
    /// ek olarak "-" ile başlayan, tırnak veya kontrol karakteri içeren değerler reddedilir.
    /// <c>bool</c> parametreler PowerShell <c>[switch]</c> olarak geçirilir (true → "-Ad", false → hiç).
    /// </summary>
    public static class RunbookParameterBinder
    {
        private const int DefaultMaxLength = 256;

        private static readonly Regex Hostname = new(
            "^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,62})(\\.[A-Za-z0-9]([A-Za-z0-9-]{0,62}))*$", RegexOptions.Compiled);

        public static BoundRunbookParameters Bind(RunbookManifest manifest, IReadOnlyDictionary<string, string?> input, OrganizationalScope scope)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var switches = new List<string>();
            var errors = new List<string>();

            foreach (var p in manifest.Parameters)
            {
                var label = string.IsNullOrWhiteSpace(p.Label) ? p.Name : p.Label;
                input.TryGetValue(p.Name, out var raw);
                var value = string.IsNullOrWhiteSpace(raw) ? p.Default : raw.Trim();

                if (p.Type == RunbookParameterTypes.Bool)
                {
                    if (string.IsNullOrWhiteSpace(value) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "on")
                        switches.Add(p.Name);
                    else
                        errors.Add($"{label}: evet/hayır olmalı.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    if (p.Required)
                        errors.Add($"{label}: zorunlu.");
                    continue;
                }

                if (value[0] == '-' || value.Any(c => c == '"' || char.IsControl(c)))
                {
                    errors.Add($"{label}: geçersiz karakter içeriyor.");
                    continue;
                }

                var error = p.Type switch
                {
                    RunbookParameterTypes.String => ValidateString(p, value),
                    RunbookParameterTypes.Int => ValidateInt(p, ref value),
                    RunbookParameterTypes.Enum => p.Values.Contains(value, StringComparer.Ordinal) ? null : "listedeki değerlerden biri olmalı.",
                    RunbookParameterTypes.AdDn => ValidateDn(p, ref value, scope),
                    RunbookParameterTypes.Hostname => Hostname.IsMatch(value) ? null : "geçerli bir sunucu adı olmalı.",
                    _ => "desteklenmeyen tür.",
                };

                if (error != null)
                    errors.Add($"{label}: {error}");
                else
                    values[p.Name] = value;
            }

            return new BoundRunbookParameters(values, switches, errors);
        }

        /// <summary>Hedef özeti (liste ekranı için): ad-dn parametrelerinin değerleri.</summary>
        public static string? Summarize(RunbookManifest manifest, BoundRunbookParameters bound)
        {
            var parts = manifest.Parameters
                .Where(p => p.Type is RunbookParameterTypes.AdDn or RunbookParameterTypes.Hostname && bound.Values.ContainsKey(p.Name))
                .Select(p => bound.Values[p.Name])
                .ToList();
            var text = string.Join("; ", parts);
            return text.Length == 0 ? null : text.Length > 500 ? text[..500] : text;
        }

        private static string? ValidateString(RunbookParameter p, string value)
        {
            if (value.Length > (p.MaxLength ?? DefaultMaxLength))
                return $"en fazla {p.MaxLength ?? DefaultMaxLength} karakter.";
            if (p.Pattern != null)
            {
                try
                {
                    if (!Regex.IsMatch(value, "^(?:" + p.Pattern + ")$", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                        return "biçim geçersiz.";
                }
                catch (RegexMatchTimeoutException)
                {
                    return "biçim doğrulanamadı.";
                }
            }
            return null;
        }

        private static string? ValidateInt(RunbookParameter p, ref string value)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return "tam sayı olmalı.";
            if (p.Min.HasValue && n < p.Min) return $"en az {p.Min}.";
            if (p.Max.HasValue && n > p.Max) return $"en fazla {p.Max}.";
            value = n.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        private static string? ValidateDn(RunbookParameter p, ref string value, OrganizationalScope scope)
        {
            if (value.Length > 1024 || !DistinguishedName.TryParse(value, out var dn))
                return "geçerli bir AD ayırt edici adı (DN) olmalı.";
            if (p.InScope && !scope.Contains(dn))
                return "yetki kapsamınızın dışında.";
            value = dn.ToString();
            return null;
        }
    }
}

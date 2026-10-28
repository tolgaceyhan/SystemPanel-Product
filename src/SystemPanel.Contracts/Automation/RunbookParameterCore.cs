using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SystemPanel.Contracts.Automation;

public sealed record BoundRunbookParameters(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> Switches,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Girdinin manifestteki parametre şemasına göre doğrulanması (sunucu ve agent ortak kullanır).
/// Değerler betiğe yalnızca <c>-File</c> + ayrı argüman olarak geçer (kod olarak yorumlanmaz);
/// ek olarak "-" ile başlayan, tırnak veya kontrol karakteri içeren değerler reddedilir.
/// <c>bool</c> parametreler PowerShell <c>[switch]</c> olarak geçirilir (true → "-Ad", false → hiç).
/// <c>ad-dn</c> doğrulaması çağırana bırakılır (sunucu: DN ayrıştırma + yetki kapsamı); verilmezse ad-dn reddedilir.
/// </summary>
public static class RunbookParameterCore
{
    public const int DefaultMaxLength = 256;

    /// <summary>ad-dn doğrulayıcı: (normalleştirilmiş değer, hata). Hata null değilse değer reddedilir.</summary>
    public delegate (string? Normalized, string? Error) DnValidator(RunbookParameter parameter, string value);

    private static readonly Regex Hostname = new(
        "^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,62})(\\.[A-Za-z0-9]([A-Za-z0-9-]{0,62}))*$", RegexOptions.Compiled);

    public static BoundRunbookParameters Bind(RunbookManifest manifest, IReadOnlyDictionary<string, string?> input, DnValidator? dnValidator)
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

            if (!IsSafeArgument(value))
            {
                errors.Add($"{label}: geçersiz karakter içeriyor.");
                continue;
            }

            string? error;
            switch (p.Type)
            {
                case RunbookParameterTypes.String:
                    error = ValidateString(p, value);
                    break;
                case RunbookParameterTypes.Int:
                    error = ValidateInt(p, ref value);
                    break;
                case RunbookParameterTypes.Enum:
                    error = p.Values.Contains(value, StringComparer.Ordinal) ? null : "listedeki değerlerden biri olmalı.";
                    break;
                case RunbookParameterTypes.Hostname:
                    error = Hostname.IsMatch(value) ? null : "geçerli bir sunucu adı olmalı.";
                    break;
                case RunbookParameterTypes.AdDn when dnValidator != null:
                    var (normalized, dnError) = dnValidator(p, value);
                    error = dnError;
                    value = normalized ?? value;
                    break;
                case RunbookParameterTypes.AdDn:
                    error = "bu çalıştırıcıda desteklenmiyor.";
                    break;
                default:
                    error = "desteklenmeyen tür.";
                    break;
            }

            if (error != null)
                errors.Add($"{label}: {error}");
            else
                values[p.Name] = value;
        }

        return new BoundRunbookParameters(values, switches, errors);
    }

    /// <summary>"-" ile başlamayan, tırnak/kontrol karakteri içermeyen, makul uzunlukta değer.</summary>
    public static bool IsSafeArgument(string value)
        => value.Length is > 0 and <= 2048 && value[0] != '-' && !value.Any(c => c == '"' || char.IsControl(c));

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
}

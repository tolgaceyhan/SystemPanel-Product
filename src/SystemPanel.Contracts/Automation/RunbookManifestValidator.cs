using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SystemPanel.Contracts.Automation;

/// <summary>Manifest yapısal doğrulaması (imzalamadan ve içe aktarmadan önce).</summary>
public static class RunbookManifestValidator
{
    public const string DryRunParameter = "DryRun";

    private static readonly Regex IdPattern = new("^[a-z0-9]+(\\.[a-z0-9-]+){1,5}$", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new("^\\d{1,4}\\.\\d{1,4}\\.\\d{1,6}$", RegexOptions.Compiled);
    private static readonly Regex ParameterName = new("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(RunbookManifest m, bool requireScriptHash = true)
    {
        var errors = new List<string>();

        if (m.FormatVersion != RunbookManifest.CurrentFormatVersion) errors.Add("Desteklenmeyen manifest sürümü.");
        if (string.IsNullOrEmpty(m.Id) || m.Id.Length > 100 || !IdPattern.IsMatch(m.Id)) errors.Add("Id geçersiz (ör. ad.user.offboard).");
        if (string.IsNullOrEmpty(m.Version) || !VersionPattern.IsMatch(m.Version)) errors.Add("Version geçersiz (ör. 1.0.0).");
        if (string.IsNullOrWhiteSpace(m.Title) || m.Title.Length > 150) errors.Add("Title gerekli (en fazla 150 karakter).");
        if (m.Description is { Length: > 2000 }) errors.Add("Description çok uzun.");
        if (!RunbookExecutors.All.Contains(m.Executor)) errors.Add("Executor desteklenmiyor.");
        if (!RunbookTargetTypes.All.Contains(m.TargetType)) errors.Add("TargetType geçersiz.");
        if (!RunbookRisks.All.Contains(m.Risk)) errors.Add("Risk geçersiz (low|medium|high).");
        if (m.TimeoutSeconds is < 10 or > 3600) errors.Add("TimeoutSeconds 10-3600 aralığında olmalı.");
        if (m.Risk == RunbookRisks.High && !m.SupportsDryRun) errors.Add("Yüksek riskli runbook önizlemeyi (SupportsDryRun) desteklemeli.");
        if (requireScriptHash && !Sha256Hex.IsMatch(m.ScriptSha256 ?? string.Empty)) errors.Add("ScriptSha256 geçersiz.");
        if (m.Parameters.Count > 30) errors.Add("En fazla 30 parametre.");
        if (m.Executor == RunbookExecutors.Agent)
        {
            if (m.TargetType != RunbookTargetTypes.Server) errors.Add("Agent runbook'larının hedef türü 'server' olmalı.");
            if (m.Parameters.Any(p => p.Type == RunbookParameterTypes.AdDn)) errors.Add("Agent runbook'ları ad-dn parametresi alamaz.");
        }
        else if (m.TargetType == RunbookTargetTypes.Server)
        {
            errors.Add("'server' hedef türü yalnızca agent çalıştırıcısıyla kullanılır.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in m.Parameters)
        {
            var prefix = $"Parametre '{p.Name}': ";
            if (!ParameterName.IsMatch(p.Name ?? string.Empty)) { errors.Add(prefix + "ad geçersiz."); continue; }
            if (string.Equals(p.Name, DryRunParameter, StringComparison.OrdinalIgnoreCase)) errors.Add(prefix + "ad ayrılmış.");
            if (!names.Add(p.Name)) errors.Add(prefix + "tekrar ediyor.");
            if (!RunbookParameterTypes.All.Contains(p.Type)) errors.Add(prefix + "tür geçersiz.");
            if (p.MaxLength is < 1 or > 2048) errors.Add(prefix + "MaxLength 1-2048 olmalı.");
            if (p.Type == RunbookParameterTypes.Enum && (p.Values.Count == 0 || p.Values.Count > 100)) errors.Add(prefix + "enum değerleri gerekli (en fazla 100).");
            if (p.Type == RunbookParameterTypes.Int && p.Min > p.Max) errors.Add(prefix + "Min > Max.");
            if (p.Pattern != null)
            {
                try { _ = new Regex(p.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { errors.Add(prefix + "Pattern geçersiz."); }
            }
        }

        return errors;
    }
}

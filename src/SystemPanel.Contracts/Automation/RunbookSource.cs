using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SystemPanel.Contracts.Automation;

/// <summary>Runbook kimliği ad alanları (ADR-002 Faz 4).</summary>
public static class RunbookNamespaces
{
    /// <summary>Kurum runbook'larına ayrılmış önek. Üretici kataloğu bu öneki kullanmaz; kurum anahtarları kullanmalıdır.</summary>
    public const string OrganizationPrefix = "org.";

    public static bool IsOrganizationId(string id)
        => id.StartsWith(OrganizationPrefix, StringComparison.Ordinal);
}

public enum RunbookLintSeverity
{
    Warning,
    Error,
}

public sealed record RunbookLintFinding(RunbookLintSeverity Severity, string Message)
{
    public override string ToString() => (Severity == RunbookLintSeverity.Error ? "HATA: " : "UYARI: ") + Message;
}

/// <summary>Kaynak klasördeki (manifest.json + run.ps1) runbook.</summary>
public sealed record RunbookSourceDirectory(string Directory, RunbookManifest Manifest, string Script);

/// <summary>
/// Runbook kaynak klasörü yükleme ve denetimi (lint). İmzalama aracı imzalamadan önce, birim testleri yerleşik ve
/// örnek runbook'lar için kullanır. Denetim bir güvenlik sınırı DEĞİLDİR (güvenlik imza + onay + kapsamla sağlanır);
/// yaygın hataları ve tehlikeli kalıpları imzalanmadan önce yakalar.
/// </summary>
public static class RunbookSource
{
    public const string ManifestFile = "manifest.json";
    public const string ScriptFile = "run.ps1";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    private static readonly Regex ParamBlock = new(@"param\s*\((?<body>.*?)\)\s*\r?\n\s*\r?\n", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Betikte bulunmaması gereken kalıplar: dinamik kod, başka süreç başlatma, ağdan içerik indirme.</summary>
    private static readonly (Regex Pattern, string Reason)[] Forbidden =
    {
        (new Regex(@"Invoke-Expression|\biex\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Invoke-Expression (dinamik kod) kullanılamaz."),
        (new Regex(@"-EncodedCommand|\[ScriptBlock\]::Create|\.InvokeScript\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Dinamik betik bloğu oluşturulamaz."),
        (new Regex(@"Start-Process|\bcmd(\.exe)?\s+/c\b|\bpowershell(\.exe)?\s+-|\bpwsh(\.exe)?\s+-", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Başka süreç/kabuk başlatılamaz."),
        (new Regex(@"DownloadString|DownloadFile|Invoke-WebRequest|Invoke-RestMethod|\biwr\b|\birm\b|Start-BitsTransfer", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Ağdan içerik indirilemez."),
        (new Regex(@"Add-Type\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Add-Type (çalışma anında derleme) kullanılamaz."),
        (new Regex(@"Set-ExecutionPolicy", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Yürütme ilkesi değiştirilemez."),
    };

    private static readonly Regex Secretish = new(@"(?i)(password|parola|secret|apikey|api_key|token)\s*=\s*['""][^'""]{4,}['""]", RegexOptions.Compiled);

    public static RunbookSourceDirectory Load(string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFile);
        var scriptPath = Path.Combine(directory, ScriptFile);
        if (!File.Exists(manifestPath))
            throw new IOException($"{directory}: {ManifestFile} bulunamadı.");
        if (!File.Exists(scriptPath))
            throw new IOException($"{directory}: {ScriptFile} bulunamadı.");

        RunbookManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RunbookManifest>(File.ReadAllText(manifestPath), ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{manifestPath}: JSON okunamadı ({ex.Message}).");
        }

        return new RunbookSourceDirectory(directory, manifest ?? throw new FormatException($"{manifestPath}: manifest boş."), File.ReadAllText(scriptPath));
    }

    /// <summary>Klasör altındaki runbook kaynakları (manifest.json içeren alt klasörler), ada göre sıralı.</summary>
    public static IReadOnlyList<string> FindDirectories(string root)
        => Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, ManifestFile)))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Kaynak denetimi. <paramref name="organization"/> true ise kurum kuralları uygulanır (kimlik <c>org.</c> ile başlamalı);
    /// false ise üretici kuralları (kimlik <c>org.</c> ile başlayamaz).
    /// </summary>
    public static IReadOnlyList<RunbookLintFinding> Lint(RunbookSourceDirectory source, bool organization)
    {
        var findings = new List<RunbookLintFinding>();
        void Error(string message) => findings.Add(new RunbookLintFinding(RunbookLintSeverity.Error, message));
        void Warn(string message) => findings.Add(new RunbookLintFinding(RunbookLintSeverity.Warning, message));

        var m = source.Manifest;
        foreach (var error in RunbookManifestValidator.Validate(m, requireScriptHash: false))
            Error(error);

        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(source.Directory));
        if (!string.Equals(folder, m.Id, StringComparison.Ordinal))
            Error($"Klasör adı ({folder}) runbook kimliğiyle ({m.Id}) aynı olmalı.");

        if (organization && !RunbookNamespaces.IsOrganizationId(m.Id))
            Error($"Kurum runbook kimliği '{RunbookNamespaces.OrganizationPrefix}' ile başlamalı (ör. org.kurum.disk.report).");
        if (!organization && RunbookNamespaces.IsOrganizationId(m.Id))
            Error($"'{RunbookNamespaces.OrganizationPrefix}' ön eki kurum runbook'larına ayrılmıştır.");

        var scriptBytes = Encoding.UTF8.GetByteCount(source.Script);
        if (scriptBytes > RunbookPackageCodec.MaxScriptBytes)
            Error($"Betik çok büyük ({scriptBytes} bayt; en fazla {RunbookPackageCodec.MaxScriptBytes}).");

        LintScript(m, source.Script, Error, Warn);

        if (string.IsNullOrWhiteSpace(m.Description))
            Warn("Description boş; katalogda ve onay ekranında ne yapıldığını açıklayın.");
        if (m.Risk != RunbookRisks.Low && !m.SupportsDryRun)
            Warn("Orta riskli runbook'larda da önizleme (SupportsDryRun) önerilir.");
        foreach (var p in m.Parameters.Where(p => p.Type == RunbookParameterTypes.String && p.Pattern == null && p.Values.Count == 0))
            Warn($"Parametre '{p.Name}': string parametrelerde Pattern tanımlayın.");

        return findings;
    }

    /// <summary>Denetimden geçen kaynağı imzalar. Denetim hatası varsa imzalamaz (<see cref="ArgumentException"/>).</summary>
    public static string Sign(RunbookSourceDirectory source, ECDsa privateKey, bool organization, out IReadOnlyList<RunbookLintFinding> findings)
    {
        findings = Lint(source, organization);
        var errors = findings.Where(f => f.Severity == RunbookLintSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new ArgumentException($"{source.Manifest.Id}: denetim hataları var, imzalanmadı:{Environment.NewLine}  " +
                string.Join(Environment.NewLine + "  ", errors.Select(e => e.Message)));

        return RunbookPackageCodec.Sign(source.Manifest, source.Script, privateKey);
    }

    private static void LintScript(RunbookManifest m, string script, Action<string> error, Action<string> warn)
    {
        var match = ParamBlock.Match(script);
        if (!match.Success)
        {
            error("Betik bir param(...) bloğuyla başlamalı ve bloktan sonra boş satır bırakılmalı.");
            return;
        }

        var body = match.Groups["body"].Value;
        foreach (var p in m.Parameters)
        {
            if (!Regex.IsMatch(body, @"\$" + Regex.Escape(p.Name) + @"\b", RegexOptions.IgnoreCase))
                error($"Parametre '{p.Name}' betiğin param() bloğunda yok.");
            else if (p.Type == RunbookParameterTypes.Bool && !Regex.IsMatch(body, @"\[switch\]\s*\$" + Regex.Escape(p.Name) + @"\b", RegexOptions.IgnoreCase))
                error($"Parametre '{p.Name}' bool olduğu için [switch] olarak tanımlanmalı.");
        }

        var declared = Regex.Matches(body, @"\$(?<name>[A-Za-z][A-Za-z0-9]*)\b")
            .Select(x => x.Groups["name"].Value)
            .Where(n => !string.Equals(n, RunbookManifestValidator.DryRunParameter, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(n, "true", StringComparison.OrdinalIgnoreCase) && !string.Equals(n, "false", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(n, "null", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in declared.Where(n => !m.Parameters.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))))
            warn($"Betik '{name}' parametresini tanımlıyor ama manifestte yok; panel bu parametreyi hiçbir zaman geçirmez.");

        var declaresDryRun = Regex.IsMatch(body, @"\[switch\]\s*\$DryRun\b", RegexOptions.IgnoreCase);
        if (m.SupportsDryRun && !declaresDryRun)
            error("SupportsDryRun=true ama betik [switch]$DryRun tanımlamıyor.");
        if (!m.SupportsDryRun && declaresDryRun)
            error("Betik -DryRun tanımlıyor ama manifestte SupportsDryRun=false.");

        foreach (var (pattern, reason) in Forbidden)
            if (pattern.IsMatch(script))
                error(reason);

        if (Secretish.IsMatch(script))
            error("Betikte sabit parola/anahtar benzeri bir değer var. Sırlar betiğe yazılmaz.");

        if (!Regex.IsMatch(script, @"Set-StrictMode|\$ErrorActionPreference\s*=\s*['""]Stop['""]", RegexOptions.IgnoreCase))
            warn("$ErrorActionPreference = 'Stop' önerilir; aksi halde hatalar sessizce geçebilir.");
    }
}

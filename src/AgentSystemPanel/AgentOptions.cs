using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AgentSystemPanel;

/// <summary>
/// "Agent" yapılandırma bölümü. Değerler appsettings.json, %ProgramData%\SystemPanel\Agent\agentsettings.json
/// ve ortam değişkenlerinden (SYSTEMPANEL_AGENT_ öneki) okunur. Sabit sunucu adresi YOKTUR.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";
    public const string DpapiPrefix = "dpapi:";

    /// <summary>SystemPanel kök adresi, ör. https://systempanel.kurum.local</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>SystemPanel'de kayıt sırasında verilen agent kimliği.</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Agent anahtarı. "dpapi:" önekliyse Windows DPAPI (LocalMachine) ile çözülür (önerilen;
    /// Install-SystemPanelAgent.ps1 bu biçimde yazar). Düz metin yalnızca geliştirme içindir.
    /// </summary>
    public string AgentKey { get; set; } = string.Empty;

    public int IntervalSeconds { get; set; } = 30;
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Etkin toplayıcılar: cpu, memory, disk.</summary>
    public List<string> EnabledCollectors { get; set; } = new() { "cpu", "memory", "disk" };

    /// <summary>Sunucuya ulaşılamazken tutulacak en fazla ölçüm (eskiler atılır).</summary>
    public int BufferMaxItems { get; set; } = 1000;

    /// <summary>Boşsa %ProgramData%\SystemPanel\Agent\buffer.</summary>
    public string? BufferDirectory { get; set; }

    public int MaxBackoffSeconds { get; set; } = 300;

    /// <summary>
    /// HTTP (şifresiz) sunucu adresine izin ver. Varsayılan false: anahtar ağda açık metin taşınmasın.
    /// Yalnızca test/geçiş için açın.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>Otomasyon (runbook) işleri. Varsayılan KAPALI.</summary>
    public AgentAutomationOptions Automation { get; set; } = new();

    internal static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("SystemPanel.Agent.Key.v1");

    /// <summary>Yapılandırmayı doğrular; sorunları Türkçe mesaj olarak döndürür (boşsa geçerli).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            errors.Add("Agent:ServerUrl geçerli bir http(s) adresi olmalıdır.");
        else if (uri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp)
            errors.Add("Agent:ServerUrl HTTPS olmalıdır (HTTP için Agent:AllowInsecureHttp=true gerekir; önerilmez).");

        if (!Guid.TryParse(AgentId, out _))
            errors.Add("Agent:AgentId geçerli bir GUID olmalıdır (SystemPanel > Agent'lar > Kaydet).");

        if (string.IsNullOrWhiteSpace(AgentKey))
            errors.Add("Agent:AgentKey tanımlı değil.");

        if (IntervalSeconds is < 5 or > 3600) errors.Add("Agent:IntervalSeconds 5-3600 olmalıdır.");
        if (TimeoutSeconds is < 2 or > 120) errors.Add("Agent:TimeoutSeconds 2-120 olmalıdır.");
        if (Automation.Enabled && Automation.PollSeconds is < 5 or > 3600) errors.Add("Agent:Automation:PollSeconds 5-3600 olmalıdır.");

        return errors;
    }

    /// <summary>Anahtarı çözer. Değer hiçbir zaman loglanmaz.</summary>
    public string ResolveKey()
    {
        if (!AgentKey.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return AgentKey;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("dpapi: korumalı anahtar yalnızca Windows'ta çözülebilir.");

        return Unprotect(AgentKey.Substring(DpapiPrefix.Length));
    }

    [SupportedOSPlatform("windows")]
    private static string Unprotect(string base64)
    {
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(base64), KeyEntropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("Agent anahtarı çözülemedi; anahtar bu makinede korunmamış olabilir. Install-SystemPanelAgent.ps1 ile yeniden yazın.", ex);
        }
    }

    public string ResolveBufferDirectory()
        => string.IsNullOrWhiteSpace(BufferDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "Agent", "buffer")
            : BufferDirectory;
}

/// <summary>
/// Agent'ta runbook çalıştırma (ADR-002 Faz 2). Sunucu ele geçirilse bile agent yalnızca:
/// <list type="bullet">
/// <item>bu bölüm açıkça etkinleştirildiyse,</item>
/// <item>runbook kimliği <see cref="AllowedRunbooks"/> listesindeyse,</item>
/// <item>paket agent'ın KENDİ güvendiği bir anahtarla imzalanmışsa</item>
/// </list>
/// iş çalıştırır.
/// </summary>
public sealed class AgentAutomationOptions
{
    public bool Enabled { get; set; }

    /// <summary>Bu sunucuda çalıştırılabilecek runbook kimlikleri (ör. "server.service.restart").</summary>
    public List<string> AllowedRunbooks { get; set; } = new();

    /// <summary>
    /// Ek güvenilen imzalama açık anahtarları (PEM dosya yolları; göreli yollar agent ayar klasörüne göre).
    /// Üretici anahtarı derlemeye gömülüdür.
    /// </summary>
    public List<string> TrustedKeyFiles { get; set; } = new();

    public int PollSeconds { get; set; } = 15;

    public string PowerShellPath { get; set; } = "powershell.exe";

    /// <summary>Boşsa %ProgramData%\SystemPanel\Agent\automation-work.</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>Sunucuya gönderilen çıktı üst sınırı (stdout ve stderr ayrı ayrı).</summary>
    public int MaxOutputBytes { get; set; } = 256 * 1024;

    public string ResolveWorkDirectory()
        => string.IsNullOrWhiteSpace(WorkDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "Agent", "automation-work")
            : WorkDirectory;
}

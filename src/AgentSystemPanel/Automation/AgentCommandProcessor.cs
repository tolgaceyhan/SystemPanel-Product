using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SystemPanel.Contracts.Agents;
using SystemPanel.Contracts.Automation;

namespace AgentSystemPanel.Automation;

/// <summary>Doğrulanmış paketi çalıştırır (gerçek: <see cref="RunbookProcessRunner"/>; testlerde sahte).</summary>
public interface IAgentScriptRunner
{
    Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct);
}

public sealed class PowerShellAgentScriptRunner : IAgentScriptRunner
{
    private readonly AgentAutomationOptions _options;
    private readonly ILogger<PowerShellAgentScriptRunner> _logger;

    public PowerShellAgentScriptRunner(Microsoft.Extensions.Options.IOptions<AgentOptions> options, ILogger<PowerShellAgentScriptRunner> logger)
    {
        _options = options.Value.Automation;
        _logger = logger;
    }

    public Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct)
        => RunbookProcessRunner.RunAsync(package, parameters, dryRun, timeout, _options.PowerShellPath, _options.ResolveWorkDirectory(),
            message => _logger.LogWarning("{Message}", message), ct);
}

/// <summary>Agent'ın güvendiği runbook imzalama açık anahtarları: gömülü üretici anahtarı + yapılandırmadaki PEM dosyaları.</summary>
public sealed class AgentTrustedKeys
{
    public const string EmbeddedVendorKey = "SystemPanel.Agent.runbook-vendor-public.pem";

    private readonly IReadOnlyList<string> _pems;

    public AgentTrustedKeys(IEnumerable<string> pems) => _pems = pems.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

    public int Count => _pems.Count;

    /// <summary>Parmak izine karşılık gelen açık anahtar (yoksa null). Çağıran dispose eder.</summary>
    public ECDsa? Find(string fingerprint)
    {
        foreach (var pem in _pems)
        {
            var key = RunbookPackageCodec.ImportPublicKeyPem(pem);
            if (string.Equals(RunbookPackageCodec.Fingerprint(key), fingerprint, StringComparison.OrdinalIgnoreCase))
                return key;
            key.Dispose();
        }
        return null;
    }

    public static AgentTrustedKeys Load(AgentAutomationOptions options, string configDirectory, ILogger logger)
    {
        var pems = new List<string>();

        using (var stream = typeof(AgentTrustedKeys).Assembly.GetManifestResourceStream(EmbeddedVendorKey))
        {
            if (stream != null)
                pems.Add(new StreamReader(stream).ReadToEnd());
        }

        foreach (var file in options.TrustedKeyFiles)
        {
            var path = Path.IsPathRooted(file) ? file : Path.Combine(configDirectory, file);
            try
            {
                var pem = File.ReadAllText(path);
                using var key = RunbookPackageCodec.ImportPublicKeyPem(pem);
                pems.Add(pem);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
            {
                logger.LogError("Güvenilen runbook anahtarı okunamadı: {Path}: {Error}", path, ex.Message);
            }
        }

        return new AgentTrustedKeys(pems);
    }
}

/// <summary>
/// Sunucudan gelen işi agent'ın KENDİ kurallarıyla doğrular ve çalıştırır. Sunucuya körü körüne güvenilmez:
/// imza (agent'ın güvendiği anahtar), runbook kimliği (yerel izin listesi), çalıştırıcı türü, önizleme desteği
/// ve parametreler (şema + enjeksiyon kuralları) burada yeniden kontrol edilir. Reddedilen iş hiç çalışmaz.
/// </summary>
public sealed class AgentCommandProcessor
{
    private readonly AgentAutomationOptions _options;
    private readonly AgentTrustedKeys _keys;
    private readonly IAgentScriptRunner _runner;
    private readonly ILogger _logger;

    public AgentCommandProcessor(AgentAutomationOptions options, AgentTrustedKeys keys, IAgentScriptRunner runner, ILogger logger)
    {
        _options = options;
        _keys = keys;
        _runner = runner;
        _logger = logger;
    }

    public async Task<AgentCommandResult> ProcessAsync(AgentCommand command, CancellationToken ct)
    {
        if (!RunbookPackageCodec.TryReadKeyId(command.Package, out var keyId, out var error))
            return Reject(command, "Paket okunamadı: " + error);

        using var key = _keys.Find(keyId!);
        if (key == null)
            return Reject(command, "Paket bu agent'ın güvendiği bir anahtarla imzalanmamış.");

        if (!RunbookPackageCodec.TryVerify(command.Package, key, out var package, out error))
            return Reject(command, "Paket doğrulanamadı: " + error);

        var manifest = package!.Manifest;
        if (manifest.Executor != RunbookExecutors.Agent)
            return Reject(command, $"{manifest.Id} agent çalıştırıcısı için değil.");

        if (!_options.AllowedRunbooks.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase))
            return Reject(command, $"{manifest.Id} bu sunucunun izin listesinde değil (Agent:Automation:AllowedRunbooks).");

        if (command.DryRun && !manifest.SupportsDryRun)
            return Reject(command, $"{manifest.Id} önizlemeyi desteklemiyor.");

        var input = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in command.Values)
            input[name] = value;
        foreach (var name in command.Switches)
            input[name] = "true";

        if (input.Keys.Any(k => manifest.Parameters.All(p => !string.Equals(p.Name, k, StringComparison.Ordinal))))
            return Reject(command, "Manifestte olmayan parametre gönderildi.");

        var bound = RunbookParameterCore.Bind(manifest, input, dnValidator: null);
        if (!bound.IsValid)
            return Reject(command, "Parametreler geçersiz: " + string.Join(" ", bound.Errors));

        var timeoutSeconds = Math.Clamp(Math.Min(command.TimeoutSeconds <= 0 ? manifest.TimeoutSeconds : command.TimeoutSeconds, manifest.TimeoutSeconds), 10, 3600);
        _logger.LogInformation("Runbook çalıştırılıyor: {Runbook}@{Version} (önizleme: {DryRun}, iş: {RunId})", manifest.Id, manifest.Version, command.DryRun, command.RunId);

        RunbookScriptResult result;
        try
        {
            result = await _runner.RunAsync(package, bound, command.DryRun, TimeSpan.FromSeconds(timeoutSeconds), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Runbook başlatılamadı: {Runbook}", manifest.Id);
            result = new RunbookScriptResult(-1, string.Empty, "Runbook başlatılamadı: " + ex.Message, false);
        }

        return new AgentCommandResult
        {
            DryRun = command.DryRun,
            ExitCode = result.ExitCode,
            TimedOut = result.TimedOut,
            StdOut = TruncateUtf8(result.StdOut, _options.MaxOutputBytes),
            StdErr = TruncateUtf8(result.StdErr, _options.MaxOutputBytes),
        };
    }

    private AgentCommandResult Reject(AgentCommand command, string reason)
    {
        _logger.LogWarning("Runbook işi reddedildi (iş {RunId}): {Reason}", command.RunId, reason);
        return new AgentCommandResult { DryRun = command.DryRun, ExitCode = -1, RejectedByAgent = true, StdErr = reason };
    }

    internal static string TruncateUtf8(string? text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text ?? string.Empty;

        var bytes = Encoding.UTF8.GetBytes(text);
        var cut = Math.Max(0, maxBytes - 64);
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
            cut--;
        return Encoding.UTF8.GetString(bytes, 0, cut) + "\n… (agent çıktıyı kısalttı)";
    }
}

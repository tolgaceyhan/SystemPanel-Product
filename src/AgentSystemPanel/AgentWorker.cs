using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using AgentSystemPanel.Collectors;
using AgentSystemPanel.Transport;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Agents;

namespace AgentSystemPanel;

/// <summary>
/// Ana döngü: topla → tampona ekle → tamponu sırayla gönder → (periyodik) heartbeat.
/// Ağ hatasında üstel bekleme uygular; kimlik reddinde (401/403) gönderimi durdurup tamponu korur.
/// Uzaktan komut ÇALIŞTIRMAZ (bkz. docs/ADR/001: allowlist'li komut modeli ROADMAP'te).
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    private const int MaxSendsPerCycle = 50;

    private readonly AgentOptions _options;
    private readonly AgentApiClient _client;
    private readonly IReadOnlyList<IMetricCollector> _collectors;
    private readonly MetricBuffer _buffer;
    private readonly ILogger<AgentWorker> _logger;
    private readonly string _version;

    private int _consecutiveFailures;
    private DateTime _nextHeartbeatUtc = DateTime.MinValue;
    private TimeSpan _interval;

    public AgentWorker(
        IOptions<AgentOptions> options,
        AgentApiClient client,
        IEnumerable<IMetricCollector> collectors,
        MetricBuffer buffer,
        ILogger<AgentWorker> logger)
    {
        _options = options.Value;
        _client = client;
        _buffer = buffer;
        _logger = logger;
        _collectors = collectors
            .Where(c => _options.EnabledCollectors.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        _interval = TimeSpan.FromSeconds(_options.IntervalSeconds);
        _version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var errors = _options.Validate();
        if (errors.Count > 0)
        {
            foreach (var e in errors) _logger.LogCritical("Yapılandırma hatası: {Error}", e);
            throw new InvalidOperationException("SystemPanel Agent yapılandırması geçersiz: " + string.Join(" ", errors));
        }

        var restored = SafeBufferIo(() => _buffer.LoadFrom(_options.ResolveBufferDirectory()));
        _logger.LogInformation("SystemPanel Agent {Version} başlıyor. Sunucu={Server} Toplayıcılar={Collectors} Tampondan yüklenen={Restored}",
            _version, _options.ServerUrl, string.Join(",", _collectors.Select(c => c.Name)), restored);

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        SafeBufferIo(() => { _buffer.SaveTo(_options.ResolveBufferDirectory()); return 0; });
        _logger.LogInformation("SystemPanel Agent durdu. Tamponda bekleyen ölçüm: {Count}", _buffer.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _buffer.Enqueue(Collect());
                await FlushAsync(stoppingToken);
                await HeartbeatIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "Beklenmeyen agent hatası.");
            }

            var delay = Backoff.Compute(_consecutiveFailures, _interval, TimeSpan.FromSeconds(_options.MaxBackoffSeconds), Random.Shared.NextDouble());
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal AgentMetricsRequest Collect()
    {
        var request = new AgentMetricsRequest { CollectedAtUtc = DateTime.UtcNow };
        foreach (var collector in _collectors)
        {
            try { collector.Collect(request); }
            catch (Exception ex) { _logger.LogWarning(ex, "{Collector} toplayıcısı başarısız.", collector.Name); }
        }
        return request;
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        for (int i = 0; i < MaxSendsPerCycle && _buffer.TryPeek(out var item) && item != null; i++)
        {
            var result = await _client.SendMetricsAsync(item, ct);
            switch (result.Outcome)
            {
                case SendOutcome.Success:
                    _buffer.TryDequeue(out _);
                    if (_consecutiveFailures > 0)
                        _logger.LogInformation("Sunucuya yeniden bağlanıldı. Kalan tampon: {Count}", _buffer.Count);
                    _consecutiveFailures = 0;
                    break;

                case SendOutcome.Rejected:
                    _buffer.TryDequeue(out _);
                    _logger.LogWarning("Sunucu ölçümü reddetti ({Status}); ölçüm atıldı.", (int?)result.StatusCode);
                    break;

                case SendOutcome.Unauthorized:
                    _consecutiveFailures++;
                    _logger.LogError("Sunucu agent kimliğini reddetti ({Status}). AgentId/anahtarı ve SystemPanel'deki kaydı kontrol edin. Ölçümler tamponda tutuluyor.", (int?)result.StatusCode);
                    return;

                default:
                    _consecutiveFailures++;
                    if (_consecutiveFailures == 1 || _consecutiveFailures % 10 == 0)
                        _logger.LogWarning("Sunucuya ulaşılamıyor ({Status}); tamponda {Count} ölçüm, atılan {Dropped}.", (int?)result.StatusCode, _buffer.Count, _buffer.Dropped);
                    return;
            }
        }
    }

    private async Task HeartbeatIfDueAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow < _nextHeartbeatUtc || _consecutiveFailures > 0)
            return;

        var result = await _client.SendHeartbeatAsync(BuildHeartbeat(), ct);
        if (result.Outcome == SendOutcome.Success)
        {
            _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(Math.Max(15, _options.HeartbeatIntervalSeconds));
            if (result.Heartbeat?.RecommendedIntervalSeconds is int recommended and >= 10 and <= 3600)
                _interval = TimeSpan.FromSeconds(Math.Max(recommended, _options.IntervalSeconds));
        }
        else if (result.Outcome == SendOutcome.Unauthorized)
        {
            _logger.LogError("Heartbeat reddedildi ({Status}).", (int?)result.StatusCode);
        }
    }

    internal AgentHeartbeatRequest BuildHeartbeat()
    {
        string? domain = null;
        try { domain = IPGlobalProperties.GetIPGlobalProperties().DomainName; } catch { }

        return new AgentHeartbeatRequest
        {
            Hostname = Environment.MachineName,
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            Fqdn = string.IsNullOrWhiteSpace(domain) ? Environment.MachineName : $"{Environment.MachineName}.{domain}",
            AgentVersion = _version,
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.Version.ToString(),
            UptimeSeconds = Environment.TickCount64 / 1000,
            SentAtUtc = DateTime.UtcNow,
        };
    }

    private int SafeBufferIo(Func<int> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Tampon dosyası okunamadı/yazılamadı.");
            return 0;
        }
    }
}

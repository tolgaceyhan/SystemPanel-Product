using AgentSystemPanel.Transport;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Agents;

namespace AgentSystemPanel.Automation;

/// <summary>
/// Sunucudan runbook işlerini çeker (agent bağlantıyı kendisi başlatır; sunucu agent'a bağlanmaz),
/// <see cref="AgentCommandProcessor"/> ile doğrulayıp çalıştırır ve sonucu bildirir. Aynı anda tek iş.
/// Agent:Automation:Enabled=false (varsayılan) ise hiçbir şey yapmaz.
/// </summary>
public sealed class AgentCommandWorker : BackgroundService
{
    private const int MaxResultAttempts = 8;

    private readonly AgentApiClient _api;
    private readonly AgentOptions _options;
    private readonly IAgentScriptRunner _runner;
    private readonly ILogger<AgentCommandWorker> _logger;
    private readonly string _configDirectory;

    public AgentCommandWorker(AgentApiClient api, IOptions<AgentOptions> options, IAgentScriptRunner runner, ILogger<AgentCommandWorker> logger)
    {
        _api = api;
        _options = options.Value;
        _runner = runner;
        _logger = logger;
        _configDirectory = AgentPaths.ConfigDirectory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var automation = _options.Automation;
        if (!automation.Enabled)
        {
            _logger.LogInformation("Otomasyon kapalı (Agent:Automation:Enabled=false); runbook işleri alınmayacak.");
            return;
        }

        var keys = AgentTrustedKeys.Load(automation, _configDirectory, _logger);
        if (keys.Count == 0)
        {
            _logger.LogError("Otomasyon açık ama güvenilen runbook anahtarı yok; işler alınmayacak (Agent:Automation:TrustedKeyFiles).");
            return;
        }
        if (automation.AllowedRunbooks.Count == 0)
            _logger.LogWarning("Otomasyon açık ama izin listesi boş (Agent:Automation:AllowedRunbooks); tüm işler reddedilecek.");

        var processor = new AgentCommandProcessor(automation, keys, _runner, _logger);
        var poll = TimeSpan.FromSeconds(Math.Clamp(automation.PollSeconds, 5, 3600));
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var (outcome, command) = await _api.GetNextCommandAsync(stoppingToken);
                switch (outcome)
                {
                    case SendOutcome.Success when command != null:
                        failures = 0;
                        var result = await processor.ProcessAsync(command, stoppingToken);
                        await ReportAsync(command.RunId, result, stoppingToken);
                        delay = TimeSpan.Zero; // kuyrukta başka iş olabilir
                        break;
                    case SendOutcome.Success:
                        failures = 0;
                        delay = poll;
                        break;
                    case SendOutcome.Unauthorized:
                        _logger.LogError("Sunucu agent kimliğini reddetti; runbook işleri alınamıyor.");
                        delay = TimeSpan.FromMinutes(5);
                        break;
                    default:
                        failures++;
                        delay = Backoff.Compute(failures, poll, TimeSpan.FromSeconds(_options.MaxBackoffSeconds), Random.Shared.NextDouble());
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogError(ex, "Runbook işi işlenirken hata.");
                delay = Backoff.Compute(failures, poll, TimeSpan.FromSeconds(_options.MaxBackoffSeconds), Random.Shared.NextDouble());
            }

            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Sonucu geçici hatalarda yeniden dener. Sonuç iletilemezse sunucu işi zaman aşımına düşürür (yeniden çalıştırmaz).</summary>
    private async Task ReportAsync(Guid runId, AgentCommandResult result, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxResultAttempts; attempt++)
        {
            var outcome = await _api.SendCommandResultAsync(runId, result, ct);
            if (outcome == SendOutcome.Success)
                return;
            if (outcome is SendOutcome.Rejected or SendOutcome.Unauthorized)
            {
                _logger.LogWarning("Sunucu iş sonucunu kabul etmedi (iş {RunId}, {Outcome}); iş zaman aşımına düşmüş olabilir.", runId, outcome);
                return;
            }

            await Task.Delay(Backoff.Compute(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(_options.MaxBackoffSeconds), Random.Shared.NextDouble()), ct);
        }

        _logger.LogError("İş sonucu sunucuya iletilemedi (iş {RunId}). Sunucu işi zaman aşımına düşürecek; hedefi kontrol edin.", runId);
    }
}

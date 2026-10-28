using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Agents;

namespace AgentSystemPanel.Transport;

public enum SendOutcome
{
    Success,

    /// <summary>Ağ hatası, zaman aşımı, 5xx, 429: daha sonra tekrar denenir.</summary>
    Transient,

    /// <summary>Kimlik reddedildi (401/403): anahtar/kayıt sorunu. Yeniden deneme yapılandırma düzelene kadar anlamsızdır.</summary>
    Unauthorized,

    /// <summary>İstek geçersiz (400 vb.): bu ölçüm atılır.</summary>
    Rejected,
}

public sealed record SendResult(SendOutcome Outcome, HttpStatusCode? StatusCode = null, AgentHeartbeatResponse? Heartbeat = null);

/// <summary>SystemPanel Agent API v1 istemcisi. Anahtar yalnızca istek başlığında taşınır, loglanmaz.</summary>
public sealed class AgentApiClient
{
    public const string HttpClientName = "SystemPanelAgent";

    private readonly IHttpClientFactory _factory;
    private readonly AgentOptions _options;
    private readonly Lazy<string> _key;

    public AgentApiClient(IHttpClientFactory factory, IOptions<AgentOptions> options)
    {
        _factory = factory;
        _options = options.Value;
        _key = new Lazy<string>(_options.ResolveKey);
    }

    public Task<SendResult> SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken ct)
        => PostAsync(AgentApi.HeartbeatPath, request, readHeartbeat: true, ct);

    public Task<SendResult> SendMetricsAsync(AgentMetricsRequest request, CancellationToken ct)
        => PostAsync(AgentApi.MetricsPath, request, readHeartbeat: false, ct);

    private async Task<SendResult> PostAsync<T>(string path, T body, bool readHeartbeat, CancellationToken ct)
    {
        var client = _factory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.ServerUrl.TrimEnd('/') + "/"), path))
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add(AgentApi.AgentIdHeader, _options.AgentId);
        message.Headers.Add(AgentApi.AgentKeyHeader, _key.Value);

        try
        {
            using var response = await client.SendAsync(message, ct);
            var outcome = Classify(response.StatusCode);

            AgentHeartbeatResponse? hb = null;
            if (outcome == SendOutcome.Success && readHeartbeat)
                hb = await response.Content.ReadFromJsonAsync<AgentHeartbeatResponse>(cancellationToken: ct);

            return new SendResult(outcome, response.StatusCode, hb);
        }
        catch (HttpRequestException)
        {
            return new SendResult(SendOutcome.Transient);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SendResult(SendOutcome.Transient); // HttpClient zaman aşımı
        }
    }

    internal static SendOutcome Classify(HttpStatusCode status)
        => (int)status switch
        {
            >= 200 and < 300 => SendOutcome.Success,
            401 or 403 => SendOutcome.Unauthorized,
            408 or 429 => SendOutcome.Transient,
            >= 500 => SendOutcome.Transient,
            _ => SendOutcome.Rejected,
        };
}

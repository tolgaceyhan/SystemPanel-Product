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

    /// <summary>Sıradaki runbook işi (ADR-002 Faz 2). İş yoksa (Success, null).</summary>
    public async Task<(SendOutcome Outcome, AgentCommand? Command)> GetNextCommandAsync(CancellationToken ct)
    {
        using var message = CreateRequest(AgentApi.NextCommandPath, content: null);
        try
        {
            using var response = await _factory.CreateClient(HttpClientName).SendAsync(message, ct);
            var outcome = Classify(response.StatusCode);
            if (outcome != SendOutcome.Success || response.StatusCode == HttpStatusCode.NoContent)
                return (outcome, null);
            return (outcome, await response.Content.ReadFromJsonAsync<AgentCommand>(cancellationToken: ct));
        }
        catch (HttpRequestException)
        {
            return (SendOutcome.Transient, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (SendOutcome.Transient, null);
        }
    }

    /// <summary>İş sonucu. 404/409 (iş artık bu agent'ın değil / zaman aşımına düştü) Rejected olarak döner.</summary>
    public async Task<SendOutcome> SendCommandResultAsync(Guid runId, AgentCommandResult result, CancellationToken ct)
    {
        using var message = CreateRequest(string.Format(AgentApi.CommandResultPathFormat, runId), JsonContent.Create(result));
        try
        {
            using var response = await _factory.CreateClient(HttpClientName).SendAsync(message, ct);
            return Classify(response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return SendOutcome.Transient;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SendOutcome.Transient;
        }
    }

    private HttpRequestMessage CreateRequest(string path, HttpContent? content)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.ServerUrl.TrimEnd('/') + "/"), path)) { Content = content };
        message.Headers.Add(AgentApi.AgentIdHeader, _options.AgentId);
        message.Headers.Add(AgentApi.AgentKeyHeader, _key.Value);
        return message;
    }

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

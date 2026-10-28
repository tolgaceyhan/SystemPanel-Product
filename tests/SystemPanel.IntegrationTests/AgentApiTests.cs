using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SystemPanel.Contracts.Agents;
using SystemPanel.Services.Agents;

namespace SystemPanel.IntegrationTests;

public sealed class AgentApiTests : IClassFixture<SystemPanelFactory>
{
    private readonly SystemPanelFactory _factory;

    public AgentApiTests(SystemPanelFactory factory) => _factory = factory;

    private async Task<AgentCredentials> RegisterAsync(string hostname)
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<AgentRegistry>();
        return await registry.RegisterAsync(hostname, "test-province", null, "integration-test");
    }

    private HttpClient AgentClient(AgentCredentials creds, string? keyOverride = null)
    {
        var client = _factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(AgentApi.AgentIdHeader, creds.AgentId.ToString());
        client.DefaultRequestHeaders.Add(AgentApi.AgentKeyHeader, keyOverride ?? creds.Key);
        return client;
    }

    private static AgentHeartbeatRequest Heartbeat(string hostname) => new()
    {
        Hostname = hostname,
        AgentVersion = "2.0.0-test",
        SentAtUtc = DateTime.UtcNow,
    };

    private static AgentMetricsRequest Metrics() => new()
    {
        CollectedAtUtc = DateTime.UtcNow,
        Cpu = new CpuMetric { UsagePercent = 12.5 },
        Memory = new MemoryMetric { UsagePercent = 40 },
        Disks = { new DiskMetric { Name = "C:", UsagePercent = 55 } },
    };

    [Fact]
    public async Task Heartbeat_WithoutCredentials_Returns401()
    {
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync("/" + AgentApi.HeartbeatPath, Heartbeat("SRV-X"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WithWrongKey_Returns401()
    {
        var creds = await RegisterAsync("SRV-WRONGKEY");
        var response = await AgentClient(creds, "spa_not-the-key").PostAsJsonAsync("/" + AgentApi.HeartbeatPath, Heartbeat("SRV-WRONGKEY"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WithValidKey_Succeeds()
    {
        var creds = await RegisterAsync("SRV-OK");
        var response = await AgentClient(creds).PostAsJsonAsync("/" + AgentApi.HeartbeatPath, Heartbeat("srv-ok"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AgentHeartbeatResponse>();
        Assert.NotNull(body);
        Assert.InRange(body!.RecommendedIntervalSeconds, 10, 3600);
    }

    [Fact]
    public async Task Heartbeat_ForAnotherHostname_Returns403()
    {
        var creds = await RegisterAsync("SRV-OWN");
        var response = await AgentClient(creds).PostAsJsonAsync("/" + AgentApi.HeartbeatPath, Heartbeat("SRV-SOMEONE-ELSE"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_WithValidKey_Returns202()
    {
        var creds = await RegisterAsync("SRV-METRICS");
        var response = await AgentClient(creds).PostAsJsonAsync("/" + AgentApi.MetricsPath, Metrics());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_WithInvalidPercent_Returns400()
    {
        var creds = await RegisterAsync("SRV-BADMETRIC");
        var metrics = Metrics();
        metrics.Cpu = new CpuMetric { UsagePercent = 500 };
        var response = await AgentClient(creds).PostAsJsonAsync("/" + AgentApi.MetricsPath, metrics);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisabledAgent_Returns401()
    {
        var creds = await RegisterAsync("SRV-DISABLED");
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AgentRegistry>().SetEnabledAsync(creds.AgentId, false);

        var response = await AgentClient(creds).PostAsJsonAsync("/" + AgentApi.HeartbeatPath, Heartbeat("SRV-DISABLED"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LegacyAnonymousIngest_IsGoneByDefault()
    {
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync("/api/agentmetrics/ingest", new { hostname = "SRV-LEGACY" });
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task AgentRegistrationPage_RequiresMonitoringManage()
    {
        var response = await _factory.CreateUserClient("Helpdesk").GetAsync("/Agents/Index");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

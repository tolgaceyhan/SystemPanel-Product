using System.Net;
using AgentSystemPanel;
using AgentSystemPanel.Collectors;
using AgentSystemPanel.Transport;
using SystemPanel.Contracts.Agents;

namespace AgentSystemPanel.Tests;

public class AgentOptionsTests
{
    private static AgentOptions Valid() => new()
    {
        ServerUrl = "https://systempanel.local",
        AgentId = Guid.NewGuid().ToString(),
        AgentKey = "spa_test",
    };

    [Fact]
    public void Valid_configuration_has_no_errors() => Assert.Empty(Valid().Validate());

    [Fact]
    public void Http_is_refused_unless_explicitly_allowed()
    {
        var o = Valid();
        o.ServerUrl = "http://systempanel.local";
        Assert.NotEmpty(o.Validate());

        o.AllowInsecureHttp = true;
        Assert.Empty(o.Validate());
    }

    [Theory]
    [InlineData("", "id", "key")]
    [InlineData("https://x", "not-a-guid", "key")]
    [InlineData("https://x", "00000000-0000-0000-0000-000000000001", "")]
    [InlineData("ftp://x", "00000000-0000-0000-0000-000000000001", "key")]
    public void Missing_or_invalid_values_are_reported(string url, string id, string key)
    {
        var o = new AgentOptions { ServerUrl = url, AgentId = id, AgentKey = key };
        Assert.NotEmpty(o.Validate());
    }

    [Fact]
    public void Plain_key_is_returned_as_is_and_dpapi_key_is_rejected_off_windows()
    {
        Assert.Equal("spa_plain", new AgentOptions { AgentKey = "spa_plain" }.ResolveKey());

        if (!OperatingSystem.IsWindows())
            Assert.Throws<PlatformNotSupportedException>(() => new AgentOptions { AgentKey = "dpapi:AAAA" }.ResolveKey());
    }
}

public class BackoffTests
{
    [Fact]
    public void No_failures_uses_base_interval()
        => Assert.Equal(TimeSpan.FromSeconds(30), Backoff.Compute(0, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), 0.5));

    [Fact]
    public void Delay_grows_and_is_capped()
    {
        var b = TimeSpan.FromSeconds(10);
        var max = TimeSpan.FromSeconds(300);

        var d1 = Backoff.Compute(1, b, max, 0.5);
        var d3 = Backoff.Compute(3, b, max, 0.5);
        var d20 = Backoff.Compute(20, b, max, 1.0);

        Assert.True(d3 > d1);
        Assert.True(d20 <= TimeSpan.FromSeconds(300 * 1.2 + 0.001));
        Assert.True(Backoff.Compute(20, b, max, 0) >= b);
    }
}

public class MetricBufferTests
{
    [Fact]
    public void Oldest_items_are_dropped_when_full()
    {
        var buffer = new MetricBuffer(3);
        for (int i = 0; i < 5; i++)
            buffer.Enqueue(new AgentMetricsRequest { CollectedAtUtc = new DateTime(2026, 1, 1).AddMinutes(i) });

        Assert.Equal(3, buffer.Count);
        Assert.Equal(2, buffer.Dropped);
        Assert.True(buffer.TryPeek(out var first));
        Assert.Equal(new DateTime(2026, 1, 1).AddMinutes(2), first!.CollectedAtUtc);
    }

    [Fact]
    public void Buffer_survives_restart_via_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp-agent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new MetricBuffer(10);
            a.Enqueue(new AgentMetricsRequest { Cpu = new CpuMetric { UsagePercent = 42 } });
            a.SaveTo(dir);

            var b = new MetricBuffer(10);
            Assert.Equal(1, b.LoadFrom(dir));
            Assert.True(b.TryPeek(out var item));
            Assert.Equal(42, item!.Cpu!.UsagePercent);
            Assert.Equal(0, new MetricBuffer(10).LoadFrom(dir)); // dosya tüketildi
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Corrupt_buffer_file_is_set_aside()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp-agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "pending-metrics.json"), "{ not json");
            Assert.Equal(0, new MetricBuffer(10).LoadFrom(dir));
            Assert.Single(Directory.GetFiles(dir, "*.corrupt-*"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class TransportAndCollectorTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, SendOutcome.Success)]
    [InlineData(HttpStatusCode.Accepted, SendOutcome.Success)]
    [InlineData(HttpStatusCode.Unauthorized, SendOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, SendOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, SendOutcome.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SendOutcome.Transient)]
    [InlineData(HttpStatusCode.BadRequest, SendOutcome.Rejected)]
    [InlineData(HttpStatusCode.Gone, SendOutcome.Rejected)]
    public void Http_status_codes_are_classified(HttpStatusCode status, SendOutcome expected)
        => Assert.Equal(expected, AgentApiClient.Classify(status));

    [Fact]
    public void Meminfo_is_parsed()
    {
        var (total, available) = MemoryCollector.ParseMemInfo(new[] { "MemTotal:       16000000 kB", "MemFree:  1000 kB", "MemAvailable:    4000000 kB" });
        Assert.Equal(16000000L * 1024, total);
        Assert.Equal(4000000L * 1024, available);
        Assert.Equal(75, MemoryCollector.Build(total, available).UsagePercent);
    }

    [Fact]
    public void Disk_collector_reports_values_in_range()
    {
        var request = new AgentMetricsRequest();
        new DiskCollector().Collect(request);

        Assert.All(request.Disks, d =>
        {
            Assert.InRange(d.UsagePercent, 0, 100);
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.True(d.Name.Length <= 32);
        });
    }
}

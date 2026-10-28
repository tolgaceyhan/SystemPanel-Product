using System.Net;
using SystemPanel.Dashboard.Core;

namespace SystemPanel.Dashboard.Tests;

public sealed class DashboardSettingsTests
{
    [Theory]
    [InlineData("https://panel.example.invalid", "https://panel.example.invalid/")]
    [InlineData("http://10.0.0.5:8080/systempanel", "http://10.0.0.5:8080/systempanel/")]
    [InlineData("  https://panel/  ", "https://panel/")]
    public void BaseUrl_IsNormalized(string input, string expected)
    {
        Assert.True(DashboardSettings.TryNormalizeBaseUrl(input, out var uri));
        Assert.Equal(expected, uri!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("panel")]
    [InlineData("file:///C:/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://panel")]
    [InlineData("https://user:pass@panel")]
    [InlineData("https://panel/?a=b")]
    public void BaseUrl_InvalidIsRejected(string? input)
        => Assert.False(DashboardSettings.TryNormalizeBaseUrl(input, out _));

    [Theory]
    [InlineData("A", true)]
    [InlineData("NOC-1_b", true)]
    [InlineData("", false)]
    [InlineData("A B", false)]
    [InlineData("A'; DROP", false)]
    [InlineData("../x", false)]
    public void ScreenCode_Validation(string screen, bool valid)
        => Assert.Equal(valid, DashboardSettings.IsValidScreenCode(screen));

    [Fact]
    public void MissingServer_IsNotComplete_NoHardCodedDefault()
    {
        var settings = new DashboardSettings { Screen = "A" };
        Assert.False(settings.IsComplete);
    }

    [Fact]
    public void ScreenConfigUri_BuildsV1AndLegacy()
    {
        var s = new DashboardSettings { Screen = "B", ApiBaseUrl = "https://panel/app" };
        Assert.Equal("https://panel/app/api/v1/dashboard/screens/B", s.ScreenConfigUri(true).ToString());
        Assert.Equal("https://panel/app/api/systemdashboard/config?screen=B", s.ScreenConfigUri(false).ToString());
    }

    [Fact]
    public void RefreshInterval_IsClamped()
    {
        Assert.Equal(TimeSpan.FromSeconds(DashboardSettings.MinRefreshSeconds), new DashboardSettings { ConfigRefreshSeconds = 1 }.RefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(DashboardSettings.MaxRefreshSeconds), new DashboardSettings { ConfigRefreshSeconds = 999999 }.RefreshInterval);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AndLegacyFileLoads()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(path, "{ \"Screen\": \"C\", \"ApiBaseUrl\": \"http://legacy-host\" }");
            var legacy = DashboardSettings.Load(path);
            Assert.Equal("C", legacy.Screen);
            Assert.True(legacy.IsComplete);
            Assert.True(legacy.UseV1Api);

            legacy.ConfigRefreshSeconds = 120;
            legacy.Save(path);
            Assert.Equal(120, DashboardSettings.Load(path).ConfigRefreshSeconds);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptySettings()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.False(DashboardSettings.Load(path).IsComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class RetryPolicyTests
{
    [Fact]
    public void Delay_GrowsAndIsCapped()
    {
        var rnd = new Random(1);
        var d1 = RetryPolicy.Delay(1, rnd);
        var d4 = RetryPolicy.Delay(4, rnd);
        var d50 = RetryPolicy.Delay(50, rnd);

        Assert.InRange(d1.TotalSeconds, 4, 6);
        Assert.InRange(d4.TotalSeconds, 32, 48);
        Assert.True(d50 <= RetryPolicy.Max);
    }
}

public sealed class DashboardConfigClientTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private string CacheFile => Path.Combine(_dir, "config-cache.json");

    public void Dispose() => Directory.Delete(_dir, true);

    private const string ValidJson = """
        { "screen": "A", "layoutType": 0, "panels": [
            { "id": 1, "title": "Grafana", "url": "https://grafana.example.invalid/d/x", "position": 1 },
            { "id": 2, "title": "Bad", "url": "javascript:alert(1)", "position": 2 },
            { "id": 3, "title": "OutOfRange", "url": "https://x.invalid", "position": 9 } ] }
        """;

    private static DashboardSettings Settings() => new() { Screen = "A", ApiBaseUrl = "https://panel.example.invalid" };

    [Fact]
    public void Parse_DropsNonHttpAndOutOfRangePanels()
    {
        var cfg = DashboardConfigClient.Parse(ValidJson);
        Assert.NotNull(cfg);
        Assert.Single(cfg!.Panels);
        Assert.Equal(1, cfg.Panels[0].Id);
    }

    [Fact]
    public async Task Success_IsLive_AndWritesCache()
    {
        var client = new DashboardConfigClient(new HttpClient(new StubHandler(_ => (HttpStatusCode.OK, ValidJson))), CacheFile);
        var result = await client.FetchAsync(Settings(), CancellationToken.None);

        Assert.Equal(ConfigFetchStatus.Live, result.Status);
        Assert.True(File.Exists(CacheFile));
    }

    [Fact]
    public async Task ServerDown_FallsBackToCache()
    {
        File.WriteAllText(CacheFile, ValidJson);
        var client = new DashboardConfigClient(new HttpClient(new StubHandler(_ => throw new HttpRequestException("down"))), CacheFile);
        var result = await client.FetchAsync(Settings(), CancellationToken.None);

        Assert.Equal(ConfigFetchStatus.Cached, result.Status);
        Assert.NotNull(result.Config);
        Assert.Contains("down", result.Error);
    }

    [Fact]
    public async Task ServerDown_NoCache_IsUnavailable()
    {
        var client = new DashboardConfigClient(new HttpClient(new StubHandler(_ => (HttpStatusCode.ServiceUnavailable, ""))), CacheFile);
        var result = await client.FetchAsync(Settings(), CancellationToken.None);

        Assert.Equal(ConfigFetchStatus.Unavailable, result.Status);
        Assert.Null(result.Config);
    }

    [Fact]
    public async Task V1NotFound_FallsBackToLegacyRoute()
    {
        var requested = new List<string>();
        var handler = new StubHandler(req =>
        {
            requested.Add(req.RequestUri!.PathAndQuery);
            return req.RequestUri!.AbsolutePath.Contains("/api/v1/")
                ? (HttpStatusCode.NotFound, "")
                : (HttpStatusCode.OK, ValidJson);
        });
        var client = new DashboardConfigClient(new HttpClient(handler), CacheFile);

        var result = await client.FetchAsync(Settings(), CancellationToken.None);
        Assert.Equal(ConfigFetchStatus.Live, result.Status);
        Assert.Equal(new[] { "/api/v1/dashboard/screens/A", "/api/systemdashboard/config?screen=A" }, requested);

        // Sonraki istekler doğrudan eski yolu kullanır.
        await client.FetchAsync(Settings(), CancellationToken.None);
        Assert.Equal("/api/systemdashboard/config?screen=A", requested[^1]);
        Assert.Equal(3, requested.Count);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _respond;

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = _respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

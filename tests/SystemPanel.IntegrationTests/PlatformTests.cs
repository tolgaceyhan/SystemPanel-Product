using System.Net;
using SystemPanel.Infrastructure;

namespace SystemPanel.IntegrationTests;

public sealed class PlatformTests : IClassFixture<SystemPanelFactory>
{
    private readonly SystemPanelFactory _factory;

    public PlatformTests(SystemPanelFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthLive_IsAnonymous()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_RequiresAuthentication()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/health/ready");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ForbiddenWithoutPermission()
    {
        var response = await _factory.CreateUserClient("NoSuchRole").GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/Gpo/Index")]
    [InlineData("/ad-users")]
    [InlineData("/Audit/Index")]
    [InlineData("/Permissions/Index")]
    [InlineData("/FileManagement/Index")]
    public async Task MvcPages_RedirectAnonymousToLogin(string path)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/systemdashboard/reorder")]
    public async Task Api_ReturnsUnauthorizedNotRedirect(string path)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(path);
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.MethodNotAllowed,
            $"Beklenmeyen durum: {response.StatusCode}");
    }

    [Fact]
    public async Task Audit_ForbiddenForRoleWithoutPermission()
    {
        var response = await _factory.CreateUserClient("Helpdesk").GetAsync("/Audit/Index");
        // Test şeması Forbid'i 403 olarak döner (cookie şeması AccessDenied'a yönlendirirdi).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Audit_AllowedForAdmin()
    {
        var response = await _factory.CreateUserClient("Admin").GetAsync("/Audit/Index");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginPage_IsAnonymous_AndHasSecurityHeaders()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
    }

    [Fact]
    public async Task LoginPage_AllCookiesAreHttpOnly_AndSecureOverHttps()
    {
        // Saha duman testinde (Test-SystemPanelDeployment.ps1) bulunan: TempData cookie'si Secure taşımıyordu.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/Account/Login");

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("SystemPanel.TempData=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, c => c.StartsWith(".AspNetCore.", StringComparison.Ordinal));
        Assert.All(cookies, c =>
        {
            Assert.Contains("httponly", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CorrelationId_IsEchoedWhenValid()
    {
        var client = _factory.CreateAnonymousClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "test-correlation-123");
        var response = await client.SendAsync(request);
        Assert.Equal("test-correlation-123", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task CorrelationId_IsGeneratedWhenMissing()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/health/live");
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single()));
    }

    [Fact]
    public async Task DashboardConfig_IsAnonymous_AndRejectsInvalidScreen()
    {
        var client = _factory.CreateAnonymousClient();

        var ok = await client.GetAsync("/api/v1/dashboard/screens/A");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var bad = await client.GetAsync("/api/v1/dashboard/screens/" + Uri.EscapeDataString("A'; DROP"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task GpoPost_WithoutAntiforgeryToken_IsRejected()
    {
        var client = _factory.CreateUserClient("Admin");
        var response = await client.PostAsync("/Gpo/Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = Guid.NewGuid().ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

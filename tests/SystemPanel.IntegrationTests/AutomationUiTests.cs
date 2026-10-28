using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemPanel.Automation;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;

namespace SystemPanel.IntegrationTests;

public sealed class AutomationUiTests : IDisposable
{
    private readonly FakeRunbookScriptHost _host = new();
    private readonly SystemPanelFactory _factory;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public AutomationUiTests()
    {
        _factory = new SystemPanelFactory(s =>
        {
            s.RemoveAll<IRunbookScriptHost>();
            s.AddSingleton<IRunbookScriptHost>(_host);
        });
        _ = _factory.Server;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _key.Dispose();
    }

    private async Task<Guid> ImportLowRiskAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var added = await scope.ServiceProvider.GetRequiredService<RunbookTrustStore>().AddAsync("Test", _key.ExportSubjectPublicKeyInfoPem(), "test");
        Assert.True(added.Ok, added.Error);

        var manifest = new RunbookManifest
        {
            Id = "test.report.stale",
            Version = "1.0.0",
            Title = "Pasif bilgisayar raporu",
            TargetType = RunbookTargetTypes.AdOu,
            Risk = RunbookRisks.Low,
            Parameters =
            {
                new RunbookParameter { Name = "SearchBase", Type = RunbookParameterTypes.AdDn, Label = "Arama kökü" },
                new RunbookParameter { Name = "Days", Type = RunbookParameterTypes.Int, Min = 7, Max = 365, Default = "90" },
            },
        };
        var result = await scope.ServiceProvider.GetRequiredService<RunbookCatalog>()
            .ImportAsync(RunbookPackageCodec.Sign(manifest, "param($SearchBase,$Days)\n'ok'", _key), "test");
        Assert.True(result.Success, result.Error);
        return result.Version!.Id;
    }

    private static async Task<string> AntiforgeryAsync(HttpClient client, string url)
    {
        var html = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    [Theory]
    [InlineData("/automation")]
    [InlineData("/automation/runs")]
    [InlineData("/automation/manage")]
    public async Task Anonymous_IsRedirectedToLogin(string url)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("Helpdesk", "/automation")]
    [InlineData("Operator", "/automation/runs")]
    [InlineData("Monitoring", "/automation/manage")]
    public async Task WithoutPermission_IsForbidden(string role, string url)
        => Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateUserClient(role).GetAsync(url)).StatusCode);

    [Theory]
    [InlineData("/automation")]
    [InlineData("/automation/runs")]
    [InlineData("/automation/approvals")]
    [InlineData("/automation/manage")]
    public async Task Admin_CanOpenPages(string url)
        => Assert.Equal(HttpStatusCode.OK, (await _factory.CreateUserClient("Admin").GetAsync(url)).StatusCode);

    [Fact]
    public async Task Admin_RequestsLowRiskRunbook_ThroughForm()
    {
        var versionId = await ImportLowRiskAsync();
        var client = _factory.CreateUserClient("Admin");
        var url = $"/automation/runbooks/{versionId}/request";

        var catalog = WebUtility.HtmlDecode(await (await client.GetAsync("/automation")).Content.ReadAsStringAsync());
        Assert.Contains("Pasif bilgisayar raporu", catalog);

        var token = await AntiforgeryAsync(client, url);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["p.SearchBase"] = "OU=Bilgisayarlar,DC=corp,DC=example",
            ["p.Days"] = "120",
            ["p.Evil"] = "ignored",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var runUrl = response.Headers.Location!.ToString();
        Assert.Contains("/automation/runs/", runUrl);

        var page = WebUtility.HtmlDecode(await (await client.GetAsync(runUrl)).Content.ReadAsStringAsync());
        Assert.Contains("Kuyrukta", page);
        Assert.Contains("120", page);
    }

    [Fact]
    public async Task InvalidParameters_AreShownOnForm()
    {
        var versionId = await ImportLowRiskAsync();
        var client = _factory.CreateUserClient("Admin");
        var url = $"/automation/runbooks/{versionId}/request";
        var token = await AntiforgeryAsync(client, url);

        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["p.SearchBase"] = "bu bir DN değil",
            ["p.Days"] = "1",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("DN", html);
        Assert.Contains("en az 7", html);
    }

    [Fact]
    public async Task RequestWithoutAntiforgery_IsRejected()
    {
        var versionId = await ImportLowRiskAsync();
        var response = await _factory.CreateUserClient("Admin").PostAsync($"/automation/runbooks/{versionId}/request",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["p.SearchBase"] = "OU=X,DC=corp,DC=example" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutScope_CannotSeeOthersRun()
    {
        var versionId = await ImportLowRiskAsync();
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RolePermissions.Add(new RolePermission { RoleName = "AutoViewer", Permission = Permissions.AutomationView });
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IRolePermissionStore>().Invalidate();

            var admin = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin1"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"),
            }, "Test"));
            var result = await scope.ServiceProvider.GetRequiredService<RunbookRunService>().RequestAsync(admin, versionId,
                new Dictionary<string, string?> { ["SearchBase"] = "OU=Gizli,DC=corp,DC=example" }, null);
            runId = result.Run!.Id;
        }

        var viewer = _factory.CreateUserClient("AutoViewer");
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/automation/runs/{runId}")).StatusCode);
        var list = await (await viewer.GetAsync("/automation/runs")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("OU=Gizli", WebUtility.HtmlDecode(list));
    }
}

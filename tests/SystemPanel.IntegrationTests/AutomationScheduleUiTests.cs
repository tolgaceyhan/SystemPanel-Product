using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemPanel.Automation;
using SystemPanel.Contracts.Automation;

namespace SystemPanel.IntegrationTests;

public sealed class AutomationScheduleUiTests : IDisposable
{
    private readonly SystemPanelFactory _factory;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public AutomationScheduleUiTests()
    {
        _factory = new SystemPanelFactory(s =>
        {
            s.RemoveAll<IRunbookScriptHost>();
            s.AddSingleton<IRunbookScriptHost>(new FakeRunbookScriptHost());
        });
        _ = _factory.Server;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _key.Dispose();
    }

    private async Task<Guid> ImportAsync(string risk = RunbookRisks.Low)
    {
        using var scope = _factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<RunbookTrustStore>().AddAsync("Test", _key.ExportSubjectPublicKeyInfoPem(), "t")).Ok);
        var manifest = new RunbookManifest
        {
            Id = "test.report.stale", Version = "1.0.0", Title = "Pasif bilgisayar raporu",
            TargetType = RunbookTargetTypes.AdOu, Risk = risk, SupportsDryRun = risk == RunbookRisks.High,
            Parameters = { new RunbookParameter { Name = "SearchBase", Type = RunbookParameterTypes.AdDn } },
        };
        var result = await scope.ServiceProvider.GetRequiredService<RunbookCatalog>()
            .ImportAsync(RunbookPackageCodec.Sign(manifest, "param($SearchBase,[switch]$DryRun)\n'ok'", _key), "t");
        Assert.True(result.Success, result.Error);
        return result.Version!.Id;
    }

    private static async Task<string> AntiforgeryAsync(HttpClient client, string url)
    {
        var html = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static Dictionary<string, string> Form(string token, string cron) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["Name"] = "Gece raporu",
        ["TriggerType"] = "cron",
        ["CronExpression"] = cron,
        ["MinIntervalMinutes"] = "60",
        ["p.SearchBase"] = "OU=Bilgisayarlar,DC=corp,DC=example",
        ["justification"] = "Haftalık envanter",
    };

    [Fact]
    public async Task Anonymous_IsRedirected()
        => Assert.Equal(HttpStatusCode.Redirect, (await _factory.CreateAnonymousClient().GetAsync("/automation/schedules")).StatusCode);

    [Fact]
    public async Task WithoutPermission_IsForbidden()
        => Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateUserClient("Helpdesk").GetAsync("/automation/schedules")).StatusCode);

    [Fact]
    public async Task Admin_CreatesLowRiskSchedule_ThroughForm()
    {
        var versionId = await ImportAsync();
        var client = _factory.CreateUserClient("Admin");
        var url = $"/automation/runbooks/{versionId}/schedule";

        var response = await client.PostAsync(url, new FormUrlEncodedContent(Form(await AntiforgeryAsync(client, url), "0 3 * * *")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = WebUtility.HtmlDecode(await (await client.GetAsync(response.Headers.Location!.ToString())).Content.ReadAsStringAsync());
        Assert.Contains("Gece raporu", page);
        Assert.Contains("Etkin", page);
        Assert.Contains("0 3 * * *", page);

        var list = WebUtility.HtmlDecode(await (await client.GetAsync("/automation/schedules")).Content.ReadAsStringAsync());
        Assert.Contains("Gece raporu", list);
    }

    [Fact]
    public async Task HighRiskSchedule_ShowsPendingApproval_AndCreatorCannotApprove()
    {
        var versionId = await ImportAsync(RunbookRisks.High);
        var client = _factory.CreateUserClient("Admin");
        var url = $"/automation/runbooks/{versionId}/schedule";

        var response = await client.PostAsync(url, new FormUrlEncodedContent(Form(await AntiforgeryAsync(client, url), "0 3 * * *")));
        var scheduleUrl = response.Headers.Location!.ToString();
        var page = WebUtility.HtmlDecode(await (await client.GetAsync(scheduleUrl)).Content.ReadAsStringAsync());

        Assert.Contains("Onay bekliyor", page);
        Assert.DoesNotContain("Zamanlamayı onayla", page); // oluşturan kişiye onay düğmesi gösterilmez

        var token = await AntiforgeryAsync(client, scheduleUrl);
        var approve = await client.PostAsync(scheduleUrl + "/approve", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        var after = WebUtility.HtmlDecode(await (await client.GetAsync(approve.Headers.Location!.ToString())).Content.ReadAsStringAsync());
        Assert.Contains("dört göz", after);
    }

    [Fact]
    public async Task InvalidCron_IsShownOnForm()
    {
        var versionId = await ImportAsync();
        var client = _factory.CreateUserClient("Admin");
        var url = $"/automation/runbooks/{versionId}/schedule";

        var response = await client.PostAsync(url, new FormUrlEncodedContent(Form(await AntiforgeryAsync(client, url), "her gün")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Cron ifadesi", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ScheduleForm_RequiresManagePermission()
    {
        var versionId = await ImportAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateUserClient("Operator").GetAsync($"/automation/runbooks/{versionId}/schedule")).StatusCode);
    }
}

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SystemPanel.Configuration;

namespace SystemPanel.IntegrationTests;

public sealed class OrganizationTests
{
    [Fact]
    public async Task MapModule_IsDisabledByDefault()
    {
        using var factory = new SystemPanelFactory();
        var client = factory.CreateUserClient("Admin");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/MapUnits/Index")).StatusCode);

        var html = WebUtility.HtmlDecode(await (await client.GetAsync("/Audit/Index")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("Haritası", html);
    }

    [Fact]
    public async Task MapModule_CanBeEnabled_AndStillRequiresPermission()
    {
        using var factory = new SystemPanelFactory(s => s.Configure<OrganizationOptions>(o => o.Map.Enabled = true));

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateUserClient("Admin").GetAsync("/MapUnits/Index")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient("Helpdesk").GetAsync("/MapUnits/Index")).StatusCode);
    }

    [Fact]
    public async Task UnitLabel_ComesFromConfiguration()
    {
        using var factory = new SystemPanelFactory(s => s.Configure<OrganizationOptions>(o => o.UnitLabel = "Bölge"));
        var html = WebUtility.HtmlDecode(await (await factory.CreateUserClient("Admin").GetAsync("/Agents/Register")).Content.ReadAsStringAsync());

        Assert.Contains("Bölge (opsiyonel)", html);
    }
}

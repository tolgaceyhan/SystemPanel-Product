using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SystemPanel.Configuration;

namespace SystemPanel.IntegrationTests;

public sealed class BrandingTests
{
    [Fact]
    public async Task ProductName_ComesFromConfiguration()
    {
        using var factory = new SystemPanelFactory(s => s.Configure<BrandingOptions>(o => o.ProductName = "Ornek Urun"));
        var response = await factory.CreateAnonymousClient().GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Giriş Yap - Ornek Urun", WebUtility.HtmlDecode(html));
    }
}

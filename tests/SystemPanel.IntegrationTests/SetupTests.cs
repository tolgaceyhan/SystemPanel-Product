using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace SystemPanel.IntegrationTests;

/// <summary>İlk kurulum sihirbazı. Her test kendi (boş) veritabanıyla çalışır.</summary>
public sealed class SetupTests
{
    private static async Task<string> AntiforgeryTokenAsync(HttpClient client)
    {
        var html = await (await client.GetAsync("/Setup")).Content.ReadAsStringAsync();
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(string antiforgery, string token, string user = "spadmin", string password = "Guclu-Parola-2026")
        => new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgery,
            ["Token"] = token,
            ["UserName"] = user,
            ["Password"] = password,
            ["ConfirmPassword"] = password,
        });

    [Fact]
    public async Task FreshInstall_ShowsSetup_AndWritesToken()
    {
        using var factory = new SystemPanelFactory();
        var client = factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Setup")).StatusCode);
        Assert.True(File.Exists(factory.SetupTokenPath));

        var login = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        Assert.Contains("İlk kurulumu başlat", WebUtility.HtmlDecode(login));
    }

    [Fact]
    public async Task WrongToken_IsRejected_NoUserCreated()
    {
        using var factory = new SystemPanelFactory();
        var client = factory.CreateAnonymousClient();
        var af = await AntiforgeryTokenAsync(client);

        var response = await client.PostAsync("/Setup", Form(af, "00000000000000000000000000000000"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("belirteci geçersiz", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().Users);
    }

    [Fact]
    public async Task CorrectToken_CreatesAdmin_ThenSetupCloses()
    {
        using var factory = new SystemPanelFactory();
        var client = factory.CreateAnonymousClient();
        var af = await AntiforgeryTokenAsync(client);
        var token = File.ReadAllText(factory.SetupTokenPath).Trim();

        var response = await client.PostAsync("/Setup", Form(af, token.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Varsayılan route giriş sayfasıdır ("/").
        Assert.Contains(response.Headers.Location!.ToString(), new[] { "/", "/Account/Login" });
        Assert.False(File.Exists(factory.SetupTokenPath));

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await users.FindByNameAsync("spadmin");
            Assert.NotNull(admin);
            Assert.True(await users.IsInRoleAsync(admin!, "Admin"));
        }

        // Kurulum bir kez yapılır.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Setup")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/Setup", Form(af, token))).StatusCode);
    }

    [Fact]
    public async Task WeakPassword_IsRejected()
    {
        using var factory = new SystemPanelFactory();
        var client = factory.CreateAnonymousClient();
        var af = await AntiforgeryTokenAsync(client);
        var token = File.ReadAllText(factory.SetupTokenPath).Trim();

        var response = await client.PostAsync("/Setup", Form(af, token, password: "kisaparola"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(factory.SetupTokenPath));
    }
}

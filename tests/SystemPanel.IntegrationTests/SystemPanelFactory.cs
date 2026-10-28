using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Data;

namespace SystemPanel.IntegrationTests;

/// <summary>
/// Uygulamayı bellekte çalıştırır: SQLite (in-memory), arka plan işleri kapalı, HTTPS zorunluluğu kapalı.
/// Kullanıcı kimliği "X-Test-Roles" başlığıyla verilir (bkz. <see cref="TestAuthHandler"/>); başlık yoksa
/// istek gerçek cookie şemasına gider (anonim davranış).
/// </summary>
public sealed class SystemPanelFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "systempanel-it-" + Guid.NewGuid().ToString("N"));

    private readonly Action<IServiceCollection>? _configureServices;

    public SystemPanelFactory() : this(null) { }

    /// <summary>Bu örneğin kurulum belirteci dosyası (her fabrika kendi geçici klasörünü kullanır).</summary>
    public string SetupTokenPath => Path.Combine(_keysPath, "setup-token.txt");

    /// <param name="configureServices">Uygulama servislerinden SONRA çalışır (servis değiştirmek için).</param>
    internal SystemPanelFactory(Action<IServiceCollection>? configureServices)
    {
        _configureServices = configureServices;
        // Program.cs bazı ayarları builder.Build() öncesinde okur; ortam değişkenleri en erken kaynaktır.
        Environment.SetEnvironmentVariable("Security__RequireHttps", "false");
        Environment.SetEnvironmentVariable("Security__DataProtectionKeysPath", _keysPath);
        Environment.SetEnvironmentVariable("BackgroundJobs__AutoHeal__Enabled", "false");
        Environment.SetEnvironmentVariable("BackgroundJobs__SccmSnapshot__Enabled", "false");
        Environment.SetEnvironmentVariable("Monitoring__Retention__Enabled", "false");
        Environment.SetEnvironmentVariable("Monitoring__RateLimitPerMinute", "1000");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=unused;Database=unused");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
            services.Configure<SystemPanel.Setup.SetupOptions>(o => o.TokenFilePath = SetupTokenPath);

            // Şema, uygulamanın hosted service'lerinden (ör. RolePermissionSeeder) ÖNCE oluşturulmalı.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService, EnsureSchemaHostedService>());

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(TestAuthHandler.Selector, TestAuthHandler.Selector, o =>
                {
                    o.ForwardDefaultSelector = ctx => ctx.Request.Headers.ContainsKey(TestAuthHandler.RolesHeader)
                        ? TestAuthHandler.SchemeName
                        : IdentityConstants.ApplicationScheme;
                });

            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.Selector;
                o.DefaultAuthenticateScheme = TestAuthHandler.Selector;
                o.DefaultChallengeScheme = TestAuthHandler.Selector;
                o.DefaultForbidScheme = TestAuthHandler.Selector;
            });

            _configureServices?.Invoke(services);
        });
    }

    public HttpClient CreateUserClient(params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        return client;
    }

    public HttpClient CreateAnonymousClient()
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            try { Directory.Delete(_keysPath, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Test kimliği: "X-Test-Roles: Admin,Operator". Boş değer = rolsüz ama kimliği doğrulanmış kullanıcı.</summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string Selector = "TestOrCookie";
    public const string RolesHeader = "X-Test-Roles";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesHeader, out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user"),
            new(ClaimTypes.Name, "test-user"),
        };
        claims.AddRange(header.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => new Claim(ClaimTypes.Role, r)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>SQLite şemasını EnsureCreated ile kurar (migration'lar SQL Server'a özgüdür).</summary>
internal sealed class EnsureSchemaHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EnsureSchemaHostedService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;
using SystemPanel.Auditing;
using SystemPanel.Configuration;
using SystemPanel.Data;
using SystemPanel.Infrastructure;
using SystemPanel.Security;
using SystemPanel.Security.Scope;
using SystemPanel.Services;
using SystemPanel.Services.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ========================
// Yapılandırma: "dpapi:" önekli değerler (Windows DPAPI LocalMachine) çözülür.
// Sırlar kaynak kontrolünde tutulmaz; bkz. docs/SECURITY.md.
// ========================
builder.Configuration.AddDpapiProtectedValues();

var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));
builder.Services.Configure<OrganizationOptions>(builder.Configuration.GetSection(OrganizationOptions.SectionName));

// ========================
// Serilog (teknik loglar; güvenlik olayları ayrıca AuditEvents tablosuna yazılır)
// ========================
builder.Host.UseSerilog((ctx, cfg) =>
{
    const string template = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console(outputTemplate: template)
       .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90, outputTemplate: template);
});

// ========================
// DataProtection: anahtarlar web klasörü dışında, Windows'ta DPAPI ile şifreli.
// (Önceden sabit C:\inetpub\... yoluna şifresiz yazılıyordu.)
// ========================
var keysPath = string.IsNullOrWhiteSpace(security.DataProtectionKeysPath)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "DataProtection-Keys")
    : security.DataProtectionKeysPath;
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("SystemPanel");
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);

// ========================
// Database
// ========================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ========================
// Identity (yerel hesaplar; AD kullanıcıları LoginService üzerinden)
// ========================
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = false;

    // Yalnızca YENİ belirlenen yerel parolaları etkiler.
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;

    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = Math.Max(3, security.MaxFailedAccessAttempts);
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, security.LockoutMinutes));
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ========================
// Cookie
// ========================
var cookieSecure = security.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "SystemPanel.Auth";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecure;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Clamp(security.SessionTimeoutMinutes, 5, 24 * 60));
    options.SlidingExpiration = true;

    // API isteklerinde login sayfasına 302 yerine 401/403 dön.
    options.Events.OnRedirectToLogin = ctx => ApiAware(ctx, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = ctx => ApiAware(ctx, StatusCodes.Status403Forbidden);

    static Task ApiAware(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
});

// Agent kimlik doğrulaması (X-Agent-Id + X-Agent-Key). Kullanıcı oturumundan (cookie) bağımsızdır.
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SystemPanel.Services.Agents.AgentAuthenticationHandler>(
        SystemPanel.Services.Agents.AgentAuthentication.Scheme, _ => { });

// Agent uçları için IP başına basit hız sınırı (kötüye kullanım / hatalı agent döngüsü koruması).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(SystemPanel.Controllers.SetupRateLimit.PolicyName, ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
    o.AddPolicy(SystemPanel.Controllers.Api.AgentRateLimit.PolicyName, ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("Monitoring:RateLimitPerMinute", 120),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "SystemPanel.Antiforgery";
    options.Cookie.SecurePolicy = cookieSecure;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "RequestVerificationToken";
});

if (security.RequireHttps)
{
    builder.Services.AddHsts(o =>
    {
        o.MaxAge = TimeSpan.FromDays(180);
        o.IncludeSubDomains = false;
    });
}

// ========================
// Authorization
// ========================
builder.Services.AddAuthorization(options =>
{
    // Varsayılan: kimliği doğrulanmamış hiçbir istek kabul edilmez. Anonim uçlar
    // [AllowAnonymous] ile AÇIKÇA işaretlenir (Login, AccessDenied, Error, health/live,
    // dashboard config, agent ingest). İzinler [RequirePermission] ile uygulanır.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// İzin (permission) bazlı yetkilendirme + organizasyonel kapsam + audit
builder.Services.AddSingleton<IRolePermissionStore, DbRolePermissionStore>();
builder.Services.AddSingleton<IPermissionService, PermissionService>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IOrganizationalScopeResolver, OrganizationalScopeResolver>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddHostedService<RolePermissionSeeder>();
builder.Services.AddHostedService<IdentityBootstrapper>();

// İlk kurulum sihirbazı (/Setup): yerel kullanıcı yokken tek kullanımlık belirteçle ilk yönetici.
builder.Services.Configure<SystemPanel.Setup.SetupOptions>(builder.Configuration.GetSection(SystemPanel.Setup.SetupOptions.SectionName));
builder.Services.AddScoped<SystemPanel.Setup.SetupService>();
builder.Services.AddHostedService<SystemPanel.Setup.SetupTokenInitializer>();

// Ürün lisansı (çevrimdışı, imzalı dosya). Bkz. docs/LICENSING.md
builder.Services.Configure<SystemPanel.Licensing.LicensingOptions>(builder.Configuration.GetSection(SystemPanel.Licensing.LicensingOptions.SectionName));
builder.Services.AddSingleton<SystemPanel.Licensing.ILicensePublicKeyProvider, SystemPanel.Licensing.EmbeddedLicensePublicKeyProvider>();
builder.Services.AddSingleton<SystemPanel.Licensing.ILicenseService, SystemPanel.Licensing.LicenseService>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<IdentityUser>, CustomUserClaimsPrincipalFactory>();

// ========================
// MVC + API hata modeli
// ========================
builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails(o =>
{
    o.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(ctx.HttpContext);
});

// ========================
// Uygulama servisleri
// ========================
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

builder.Services.Configure<LdapOptions>(builder.Configuration.GetSection("Ldap"));
builder.Services.Configure<ActiveDirectoryOptions>(builder.Configuration.GetSection(ActiveDirectoryOptions.SectionName));
builder.Services.Configure<RoleMappingOptions>(builder.Configuration.GetSection(RoleMappingOptions.SectionName));
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
builder.Services.Configure<SystemPanel.Services.Gpo.GpoOptions>(builder.Configuration.GetSection(SystemPanel.Services.Gpo.GpoOptions.SectionName));
builder.Services.Configure<SccmOptions>(builder.Configuration.GetSection(SccmOptions.SectionName));

// Active Directory — işlemler YALNIZCA *Operations facade'ları üzerinden (kapsam + audit)
builder.Services.AddScoped<IAdUserService, AdUserService>();
builder.Services.AddScoped<IAdComputerService, AdComputerService>();
builder.Services.AddScoped<IAdDirectoryService, AdDirectoryService>();
builder.Services.AddScoped<SystemPanel.Services.ActiveDirectory.IAdUserOperations, SystemPanel.Services.ActiveDirectory.AdUserOperations>();
builder.Services.AddScoped<SystemPanel.Services.ActiveDirectory.IAdComputerOperations, SystemPanel.Services.ActiveDirectory.AdComputerOperations>();
builder.Services.AddScoped<LoginService>();

// GPO
builder.Services.AddSingleton<SystemPanel.Services.Gpo.IPowerShellScriptRunner, SystemPanel.Services.Gpo.PowerShellScriptRunner>();
builder.Services.AddScoped<IGpoService, GpoService>();

// SCCM
builder.Services.AddScoped<SccmSnapshotService>();
builder.Services.AddScoped<SccmLogService>();
builder.Services.AddScoped<IDpServiceStatusService, DpServiceStatusService>();

// İzleme / agent ingestion
builder.Services.Configure<SystemPanel.Services.Agents.MonitoringOptions>(builder.Configuration.GetSection(SystemPanel.Services.Agents.MonitoringOptions.SectionName));
builder.Services.AddScoped<SystemPanel.Services.Agents.IMetricsIngestionService, SystemPanel.Services.Agents.MetricsIngestionService>();
builder.Services.AddScoped<SystemPanel.Services.Agents.AgentRegistry>();
builder.Services.AddHostedService<SystemPanel.Services.Agents.MetricsRetentionService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IAgentStatusService, AgentStatusService>();
builder.Services.AddScoped<ServerMonitoringService>();
builder.Services.AddScoped<IServiceStatusService, ServiceStatusService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<IMapUnitService, MapUnitService>();
builder.Services.AddScoped<AutoHealService>();

// Diğer
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<ISmsLogService, SmsLogService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IAnnouncementsService, AnnouncementsService>();
builder.Services.AddScoped<FileManagementService>();
builder.Services.AddScoped<IFileManagementService>(sp => sp.GetRequiredService<FileManagementService>());
builder.Services.AddSingleton<IMailService, MailService>();

// Arka plan işleri (BackgroundJobs:<Ad>:Enabled ile kapatılabilir; testlerde kapalıdır)
if (builder.Configuration.GetValue("BackgroundJobs:AutoHeal:Enabled", true))
    builder.Services.AddHostedService<AutoHealWorker>();
if (builder.Configuration.GetValue("BackgroundJobs:SccmSnapshot:Enabled", true))
    builder.Services.AddHostedService<SccmDpSnapshotWorker>();

// ========================
// Health checks
// ========================
builder.Services.AddSingleton<LdapProbeService>();
builder.Services.AddHealthChecks()
    .AddCheck<LdapHealthCheck>("ldap", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddCheck<SqlServerHealthCheck>("db", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddCheck<FileSystemHealthCheck>("filesystem", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

// ========================
// Swagger (yalnızca Development)
// ========================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SystemPanel API", Version = "v1" });
});

var app = builder.Build();

if (!security.RequireHttps)
{
    app.Logger.LogWarning("Security:RequireHttps=false — oturum cookie'leri HTTP üzerinden gönderilebilir. Yalnızca geçici geçiş için kullanın.");
}

// ========================
// Middleware
// ========================
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Kullanıcı teknik ayrıntı görmez: MVC'de dostça sayfa + correlation ID, API'de ProblemDetails
    // (ayrım HomeController.Error içinde). Re-execute aynı isteği kullandığı için correlation ID korunur.
    app.UseExceptionHandler("/Home/Error");
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
        b => b.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}"));

    if (security.RequireHttps)
        app.UseHsts();
}

if (security.RequireHttps)
    app.UseHttpsRedirection();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ========================
// Health
//   /health/live  : anonim, yalnızca "uygulama ayakta" (yük dengeleyici / izleme)
//   /health/ready : settings.view; bağımlılık ayrıntıları (önceden anonimdi ve exception mesajı döndürüyordu)
// ========================
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = (ctx, rpt) =>
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { status = rpt.Status.ToString() }));
    }
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = async (ctx, rpt) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = rpt.Status.ToString(),
            checks = rpt.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (int)e.Value.Duration.TotalMilliseconds,
            })
        }));
    }
}).RequireAuthorization(new RequirePermissionAttribute(Permissions.SettingsView).Policy!);

// ========================
// Routes
// ========================
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

/// <summary>WebApplicationFactory (entegrasyon testleri) için.</summary>
public partial class Program { }

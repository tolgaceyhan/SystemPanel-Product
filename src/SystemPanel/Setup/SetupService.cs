using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Data;

namespace SystemPanel.Setup
{
    public sealed class SetupOptions
    {
        public const string SectionName = "Setup";

        /// <summary>Boşsa %ProgramData%\SystemPanel\setup-token.txt</summary>
        public string TokenFilePath { get; set; } = string.Empty;

        public string ResolveTokenFilePath()
            => string.IsNullOrWhiteSpace(TokenFilePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "setup-token.txt")
                : TokenFilePath;
    }

    public sealed record SetupResult(bool Success, string? Error);

    /// <summary>
    /// İlk kurulum: veritabanında hiç yerel kullanıcı yokken ilk yöneticinin web arayüzünden oluşturulması.
    /// Güvenlik: sayfa yalnızca kurulum gerekiyorsa açılır ve sunucudaki kurulum belirtecini (yalnızca
    /// yöneticilerin okuyabildiği bir dosya) bilen kişi ilk yöneticiyi oluşturabilir. Belirteç tek kullanımlıktır.
    /// </summary>
    public sealed class SetupService
    {
        public const string AdminRole = "Admin";

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _users;
        private readonly RoleManager<IdentityRole> _roles;
        private readonly SetupOptions _options;
        private readonly ILogger<SetupService> _logger;

        private static readonly SemaphoreSlim Gate = new(1, 1);

        public SetupService(ApplicationDbContext db, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles,
            IOptions<SetupOptions> options, ILogger<SetupService> logger)
        {
            _db = db;
            _users = users;
            _roles = roles;
            _options = options.Value;
            _logger = logger;
        }

        public string TokenFilePath => _options.ResolveTokenFilePath();

        /// <summary>Hiç yerel (Identity) kullanıcı yoksa kurulum gereklidir. Veritabanına erişilemiyorsa false.</summary>
        public async Task<bool> IsSetupRequiredAsync(CancellationToken ct = default)
        {
            try
            {
                return !await _db.Users.AnyAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Kurulum durumu okunamadı (veritabanı hazır değil olabilir).");
                return false;
            }
        }

        /// <summary>Kurulum gerekiyorsa ve belirteç dosyası yoksa yeni bir belirteç üretir.</summary>
        public async Task EnsureTokenAsync(CancellationToken ct = default)
        {
            if (!await IsSetupRequiredAsync(ct))
                return;

            var path = TokenFilePath;
            if (File.Exists(path))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            await File.WriteAllTextAsync(path, token + Environment.NewLine, Encoding.ASCII, ct);
            RestrictToAdministrators(path);

            _logger.LogWarning("İlk kurulum gerekli: yönetici hesabı yok. Kurulum belirteci şu dosyaya yazıldı: {Path}. /Setup sayfasını açın.", path);
        }

        public async Task<SetupResult> CompleteAsync(string token, string userName, string password, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct);
            try
            {
                if (!await IsSetupRequiredAsync(ct))
                    return new SetupResult(false, "Kurulum zaten tamamlanmış.");

                if (!VerifyToken(token))
                    return new SetupResult(false, "Kurulum belirteci geçersiz.");

                if (!await _roles.RoleExistsAsync(AdminRole))
                    await _roles.CreateAsync(new IdentityRole(AdminRole));

                var user = new IdentityUser { UserName = userName.Trim(), EmailConfirmed = true };
                var created = await _users.CreateAsync(user, password);
                if (!created.Succeeded)
                    return new SetupResult(false, string.Join(" ", created.Errors.Select(e => e.Description)));

                await _users.AddToRoleAsync(user, AdminRole);

                try { File.Delete(TokenFilePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Kurulum belirteci silinemedi; dosyayı elle silin: {Path}", TokenFilePath);
                }

                return new SetupResult(true, null);
            }
            finally
            {
                Gate.Release();
            }
        }

        private bool VerifyToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token) || !File.Exists(TokenFilePath))
                return false;

            var expected = File.ReadAllText(TokenFilePath).Trim();
            var a = Encoding.ASCII.GetBytes(expected);
            var b = Encoding.ASCII.GetBytes(token.Trim().ToUpperInvariant());
            return a.Length > 0 && CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>Windows'ta dosyayı yalnızca Administrators, SYSTEM ve uygulama kimliği okuyabilir.</summary>
        private static void RestrictToAdministrators(string path)
        {
            if (!OperatingSystem.IsWindows())
                return;

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            var current = WindowsIdentity.GetCurrent().User;
            if (current != null)
                security.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
    }
}

namespace SystemPanel.Setup
{
    /// <summary>Açılışta, kurulum gerekiyorsa kurulum belirtecini üretir.</summary>
    public sealed class SetupTokenInitializer : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopes;
        private readonly ILogger<SetupTokenInitializer> _logger;

        public SetupTokenInitializer(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes, ILogger<SetupTokenInitializer> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetRequiredService<SetupService>(scope.ServiceProvider).EnsureTokenAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Kurulum belirteci yazılamadı.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

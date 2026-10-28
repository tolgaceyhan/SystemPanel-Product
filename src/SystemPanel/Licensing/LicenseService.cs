using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Licensing;

namespace SystemPanel.Licensing
{
    public sealed class LicensingOptions
    {
        public const string SectionName = "Licensing";

        /// <summary>Boşsa %ProgramData%\SystemPanel\license.lic</summary>
        public string LicenseFilePath { get; set; } = string.Empty;

        public string ResolveLicenseFilePath()
            => string.IsNullOrWhiteSpace(LicenseFilePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "license.lic")
                : LicenseFilePath;
    }

    /// <summary>Lisanssız/geçersiz durumda kayıtlı agent üst sınırı (değerlendirme kullanımı).</summary>
    public static class LicenseLimits
    {
        public const int UnlicensedMaxAgents = 5;
    }

    /// <summary>Doğrulama açık anahtarının kaynağı. Ürün yapısında derlemeye gömülüdür; yapılandırmadan DEĞİŞTİRİLEMEZ.</summary>
    public interface ILicensePublicKeyProvider
    {
        /// <summary>Gömülü anahtar yoksa null (geliştirme yapısı).</summary>
        string? GetPublicKeyPem();
    }

    public sealed class EmbeddedLicensePublicKeyProvider : ILicensePublicKeyProvider
    {
        public const string ResourceName = "SystemPanel.Licensing.license-public.pem";

        public string? GetPublicKeyPem()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    public interface ILicenseService
    {
        LicenseStatus GetStatus();

        /// <summary>Yeni lisans dosyasını doğrular ve kurar. Geçersizse hiçbir şey yazılmaz.</summary>
        Task<LicenseStatus> InstallAsync(string licenseText, CancellationToken ct = default);

        /// <summary>Lisansa göre izin verilen en fazla agent sayısı (null = sınırsız).</summary>
        int? MaxAgents { get; }
    }

    /// <summary>
    /// Lisans politikası (kritik altyapı yazılımı olduğu için bilinçli olarak yumuşak):
    /// lisans sorunu hiçbir zaman mevcut işlemleri, oturum açmayı veya izlemeyi DURDURMAZ.
    /// Yalnızca uyarı gösterilir ve yeni agent kaydı sınırlandırılır.
    /// </summary>
    public sealed class LicenseService : ILicenseService
    {
        private readonly ILicensePublicKeyProvider _keys;
        private readonly LicensingOptions _options;
        private readonly ILogger<LicenseService> _logger;
        private readonly TimeProvider _time;
        private readonly object _sync = new();

        private LicenseInfo? _cachedLicense;
        private LicenseStatus? _cachedFailure;
        private DateTime _cachedFileStamp = DateTime.MinValue;
        private bool _loaded;

        public LicenseService(ILicensePublicKeyProvider keys, IOptions<LicensingOptions> options, ILogger<LicenseService> logger, TimeProvider? time = null)
        {
            _keys = keys;
            _options = options.Value;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        public int? MaxAgents
        {
            get
            {
                var status = GetStatus();
                return status.State switch
                {
                    LicenseState.NotConfigured => null,
                    LicenseState.Valid or LicenseState.GracePeriod => status.License!.MaxAgents,
                    _ => LicenseLimits.UnlicensedMaxAgents,
                };
            }
        }

        public LicenseStatus GetStatus()
        {
            var pem = _keys.GetPublicKeyPem();
            if (string.IsNullOrWhiteSpace(pem))
                return new LicenseStatus(LicenseState.NotConfigured, null, "Bu yapıda lisans doğrulaması yapılandırılmamış (geliştirme yapısı).");

            var path = _options.ResolveLicenseFilePath();
            lock (_sync)
            {
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (!_loaded || stamp != _cachedFileStamp)
                {
                    Load(pem, path);
                    _cachedFileStamp = stamp;
                    _loaded = true;
                }

                return _cachedLicense != null
                    ? LicenseEvaluator.Evaluate(_cachedLicense, _time.GetUtcNow().UtcDateTime)
                    : _cachedFailure!;
            }
        }

        public async Task<LicenseStatus> InstallAsync(string licenseText, CancellationToken ct = default)
        {
            var pem = _keys.GetPublicKeyPem();
            if (string.IsNullOrWhiteSpace(pem))
                return new LicenseStatus(LicenseState.NotConfigured, null, "Bu yapı lisans dosyası kabul etmez (doğrulama anahtarı yok).");

            using var key = LicenseCodec.ImportPublicKeyPem(pem);
            if (!LicenseCodec.TryVerify(licenseText, key, out var license, out var error))
                return new LicenseStatus(LicenseState.Invalid, null, error);

            var path = _options.ResolveLicenseFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, licenseText, ct);
            File.Move(tmp, path, overwrite: true);

            lock (_sync)
            {
                _loaded = false;
            }

            _logger.LogInformation("Lisans kuruldu: {LicenseId} ({Customer}, {Edition}).", license!.LicenseId, license.Customer, license.Edition);
            return GetStatus();
        }

        private void Load(string pem, string path)
        {
            _cachedLicense = null;
            _cachedFailure = null;

            if (!File.Exists(path))
            {
                _cachedFailure = new LicenseStatus(LicenseState.Missing, null, "Lisans dosyası yüklenmemiş. Değerlendirme sınırları geçerli.");
                return;
            }

            try
            {
                using var key = LicenseCodec.ImportPublicKeyPem(pem);
                var text = File.ReadAllText(path);
                if (LicenseCodec.TryVerify(text, key, out var license, out var error))
                {
                    _cachedLicense = license;
                    return;
                }

                _logger.LogWarning("Lisans dosyası geçersiz: {Error}", error);
                _cachedFailure = new LicenseStatus(LicenseState.Invalid, null, error);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
            {
                _logger.LogWarning(ex, "Lisans dosyası okunamadı.");
                _cachedFailure = new LicenseStatus(LicenseState.Invalid, null, "Lisans dosyası okunamadı.");
            }
        }
    }
}

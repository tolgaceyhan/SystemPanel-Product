using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SystemPanel.Dashboard.Core
{
    /// <summary>
    /// %ProgramData%\SystemPanel\Dashboard\appsettings.json. Sunucu adresi koda gömülü DEĞİLDİR
    /// (önceden "http://systempanel" varsayılırdı); ilk kurulumda girilir.
    /// </summary>
    public sealed class DashboardSettings
    {
        public const int MinRefreshSeconds = 15;
        public const int MaxRefreshSeconds = 3600;

        public string? Screen { get; set; }
        public string? ApiBaseUrl { get; set; }

        /// <summary>Panel yapılandırmasının sunucudan yeniden okunma aralığı.</summary>
        public int ConfigRefreshSeconds { get; set; } = 60;

        /// <summary>true: /api/v1/dashboard/screens/{screen}; sunucu eski sürümse otomatik olarak eski yola düşülür.</summary>
        public bool UseV1Api { get; set; } = true;

        public bool IsComplete => Validate() == null;

        public TimeSpan RefreshInterval
            => TimeSpan.FromSeconds(Math.Clamp(ConfigRefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds));

        /// <summary>Geçerliyse null, değilse kullanıcıya gösterilecek açıklama.</summary>
        public string? Validate()
        {
            if (!IsValidScreenCode(Screen))
                return "Ekran kodu geçersiz (harf, rakam, '-' veya '_'; en fazla 32 karakter).";
            if (!TryNormalizeBaseUrl(ApiBaseUrl, out _))
                return "Sunucu adresi geçersiz. Örnek: https://sunucu-adi (yalnızca http/https).";
            return null;
        }

        /// <summary>Sunucudaki DashboardPanelRules.IsValidScreenCode ile aynı kural.</summary>
        public static bool IsValidScreenCode(string? screen)
            => !string.IsNullOrWhiteSpace(screen)
               && screen.Length <= 32
               && screen.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

        public static bool TryNormalizeBaseUrl(string? value, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
                return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
                return false;
            if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment) || !string.IsNullOrEmpty(parsed.UserInfo))
                return false;

            var text = parsed.GetLeftPart(UriPartial.Path);
            uri = new Uri(text.EndsWith('/') ? text : text + "/");
            return true;
        }

        public Uri ScreenConfigUri(bool v1)
        {
            if (!TryNormalizeBaseUrl(ApiBaseUrl, out var baseUri) || !IsValidScreenCode(Screen))
                throw new InvalidOperationException("Ayarlar eksik veya geçersiz.");

            var screen = Uri.EscapeDataString(Screen!);
            return v1
                ? new Uri(baseUri!, "api/v1/dashboard/screens/" + screen)
                : new Uri(baseUri!, "api/systemdashboard/config?screen=" + screen);
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public static DashboardSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new DashboardSettings();

                return JsonSerializer.Deserialize<DashboardSettings>(File.ReadAllText(path), JsonOptions) ?? new DashboardSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                DashboardLog.Error("Ayar dosyası okunamadı: " + path, ex);
                return new DashboardSettings();
            }
        }

        /// <summary>Atomik yazım: önce geçici dosya, sonra yer değiştirme.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }
}

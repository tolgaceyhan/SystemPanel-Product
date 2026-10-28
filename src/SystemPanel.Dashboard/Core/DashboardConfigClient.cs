using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Dashboard.Models;

namespace SystemPanel.Dashboard.Core
{
    public enum ConfigFetchStatus
    {
        /// <summary>Sunucudan geçerli yapılandırma alındı.</summary>
        Live,
        /// <summary>Sunucuya ulaşılamadı; diskteki son geçerli yapılandırma kullanılıyor.</summary>
        Cached,
        /// <summary>Sunucuya ulaşılamadı ve önbellek yok.</summary>
        Unavailable,
    }

    public sealed record ConfigFetchResult(ConfigFetchStatus Status, DashboardConfig? Config, string? Error, string? Json);

    /// <summary>
    /// Panel yapılandırmasını sunucudan okur. Başarılı yanıtlar diske önbelleklenir; böylece sunucu
    /// kapalıyken açılan kiosk ekranı son bilinen panelleri gösterebilir.
    /// </summary>
    public sealed class DashboardConfigClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _http;
        private readonly string _cacheFile;
        private bool _v1Unsupported;

        public DashboardConfigClient(HttpClient http, string cacheFile)
        {
            _http = http;
            _cacheFile = cacheFile;
        }

        public async Task<ConfigFetchResult> FetchAsync(DashboardSettings settings, CancellationToken ct)
        {
            string? error;
            try
            {
                var useV1 = settings.UseV1Api && !_v1Unsupported;
                using var response = await _http.GetAsync(settings.ScreenConfigUri(useV1), ct).ConfigureAwait(false);

                if (useV1 && response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Eski sunucu sürümü: v1 yolu yok. Bu oturumda eski yolu kullan.
                    _v1Unsupported = true;
                    DashboardLog.Warn("v1 dashboard API bulunamadı; eski yola geçiliyor.");
                    return await FetchAsync(settings, ct).ConfigureAwait(false);
                }

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var config = Parse(json);
                    if (config != null)
                    {
                        TryWriteCache(json);
                        return new ConfigFetchResult(ConfigFetchStatus.Live, config, null, json);
                    }
                    error = "Sunucu yanıtı çözümlenemedi.";
                }
                else
                {
                    error = $"Sunucu {(int)response.StatusCode} döndü.";
                }
            }
            catch (HttpRequestException ex)
            {
                error = "Sunucuya ulaşılamadı: " + ex.Message;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                error = "Sunucu zaman aşımına uğradı.";
            }

            var cached = TryReadCache();
            return cached != null
                ? new ConfigFetchResult(ConfigFetchStatus.Cached, cached.Value.Config, error, cached.Value.Json)
                : new ConfigFetchResult(ConfigFetchStatus.Unavailable, null, error, null);
        }

        public static DashboardConfig? Parse(string json)
        {
            try
            {
                var config = JsonSerializer.Deserialize<DashboardConfig>(json, JsonOptions);
                if (config?.Panels == null)
                    return null;

                // Yalnızca http/https paneller ve 1-4 konumları (sunucu da filtreler; savunma derinliği).
                config.Panels.RemoveAll(p =>
                    p.Position is < 1 or > 4 ||
                    !Uri.TryCreate(p.Url, UriKind.Absolute, out var u) ||
                    (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps));
                return config;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void TryWriteCache(string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
                var tmp = _cacheFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _cacheFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DashboardLog.Warn("Yapılandırma önbelleği yazılamadı: " + ex.Message);
            }
        }

        private (DashboardConfig Config, string Json)? TryReadCache()
        {
            try
            {
                if (!File.Exists(_cacheFile))
                    return null;
                var json = File.ReadAllText(_cacheFile);
                var config = Parse(json);
                return config == null ? null : (config, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SystemPanel.Services
{
    public class SmsService : ISmsService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<SmsService> _logger;

        public SmsService(HttpClient http, IConfiguration config, ILogger<SmsService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string phone, string message)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return;

            var sms = _config.GetSection("SMS");

            var apiUrl = sms["ApiUrl"];                 // mevcut yapın
            var apiKeyName = sms["ApiKeySecretName"];   // mevcut yapın

            if (string.IsNullOrWhiteSpace(apiUrl))
                throw new InvalidOperationException("SMS:ApiUrl tanımlı değil.");

            // 1) LEGACY GET (eski sistemle uyum)
            // Eski örnek: http://200.200.200.237/SMSBTServisi/sms_send.aspx?num=...&msg=...
            // Burada ApiUrl'yi base olarak da verebilirsin:
            // http://200.200.200.237/SMSBTServisi/sms_send.aspx
            if (apiUrl.Contains("sms_send.aspx", StringComparison.OrdinalIgnoreCase))
            {
                var num = Uri.EscapeDataString(phone);
                var msg = Uri.EscapeDataString(message ?? "");

                // ApiUrl zaten querystring içeriyorsa (&?) düzgün bağla
                var sep = apiUrl.Contains("?") ? "&" : "?";
                var url = $"{apiUrl}{sep}num={num}&msg={msg}";

                var result = await _http.GetStringAsync(url);

                if (string.IsNullOrWhiteSpace(result) ||
                    !result.Contains("OK", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Legacy SMS başarısız. Response: {Resp}", result);
                    throw new Exception($"SMS gönderilemedi (Legacy). Response: {result}");
                }

                _logger.LogInformation("SMS (Legacy) gönderildi → {Phone}", phone);
                return;
            }

            // 2) Mevcut POST JSON akışın (aynen)
            var apiKey = string.IsNullOrWhiteSpace(apiKeyName)
                ? null
                : Environment.GetEnvironmentVariable(apiKeyName);

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("SMS API key not found in environment variable: {KeyName}", apiKeyName);
                throw new InvalidOperationException("SMS API key not found in environment variables.");
            }

            var payload = new
            {
                to = phone,
                text = message
            };

            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Add("X-API-KEY", apiKey);

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("SMS gönderimi başarısız: {Error}", error);
                throw new Exception($"SMS gönderilemedi: {error}");
            }

            _logger.LogInformation("SMS gönderildi → {Phone}", phone);
        }
    }
}

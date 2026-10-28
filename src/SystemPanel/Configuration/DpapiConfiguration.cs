using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SystemPanel.Configuration
{
    /// <summary>
    /// "dpapi:" önekli yapılandırma değerlerini Windows DPAPI (LocalMachine kapsamı) ile çözer.
    /// Değerler herhangi bir kaynaktan gelebilir (appsettings.Production.json, ortam değişkeni ...).
    /// Şifreleme: scripts/Protect-SystemPanelSecret.ps1 (aynı makinede çalıştırılmalıdır;
    /// DPAPI LocalMachine ile korunan değer yalnızca o makinede çözülebilir).
    ///
    /// Bulut bağımlılığı yoktur; kapalı ağda çalışır. Sır deposu ihtiyacı büyürse ROADMAP'e bakın.
    /// </summary>
    public static class DpapiConfiguration
    {
        public const string Prefix = "dpapi:";

        /// <summary>DPAPI ek entropisi: sırrı değil, değerin hangi uygulamaya ait olduğunu bağlar.</summary>
        internal static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SystemPanel.Configuration.v1");

        public static IConfigurationBuilder AddDpapiProtectedValues(this IConfigurationManager configuration)
        {
            var protectedValues = configuration.AsEnumerable()
                .Where(kv => kv.Value != null && kv.Value.StartsWith(Prefix, StringComparison.Ordinal))
                .ToList();

            if (protectedValues.Count == 0)
                return configuration;

            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(
                    $"'{Prefix}' ile korunan yapılandırma değerleri yalnızca Windows'ta çözülebilir: {string.Join(", ", protectedValues.Select(kv => kv.Key))}");

            var decrypted = protectedValues.ToDictionary(kv => kv.Key, kv => (string?)Unprotect(kv.Key, kv.Value!));
            configuration.AddInMemoryCollection(decrypted);
            return configuration;
        }

        [SupportedOSPlatform("windows")]
        private static string Unprotect(string key, string value)
        {
            try
            {
                var bytes = Convert.FromBase64String(value.Substring(Prefix.Length));
                var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                // Değerin kendisi mesaja yazılmaz.
                throw new InvalidOperationException($"Korumalı yapılandırma değeri çözülemedi: {key}. Değer bu makinede şifrelenmemiş olabilir.", ex);
            }
        }
    }
}

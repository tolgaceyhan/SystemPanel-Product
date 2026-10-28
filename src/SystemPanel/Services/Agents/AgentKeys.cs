using System;
using System.Security.Cryptography;
using System.Text;

namespace SystemPanel.Services.Agents
{
    /// <summary>Agent gizli anahtarı üretimi ve doğrulaması.</summary>
    public static class AgentKeys
    {
        public const string Prefix = "spa_";

        /// <summary>256 bit rastgele, URL-güvenli anahtar üretir. Değer yalnızca bir kez gösterilir.</summary>
        public static string Generate()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Anahtar yüksek entropili olduğundan tuzsuz SHA-256 yeterlidir (parola değildir; sözlük saldırısı uygulanamaz).
        /// </summary>
        public static string Hash(string key)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        public static string DisplayPrefix(string key) => key.Length <= 12 ? key : key.Substring(0, 12);

        /// <summary>Sabit zamanlı karşılaştırma (zamanlama saldırısına karşı).</summary>
        public static bool Verify(string? presentedKey, string storedHash)
        {
            if (string.IsNullOrEmpty(presentedKey) || presentedKey.Length > 256 || string.IsNullOrEmpty(storedHash))
                return false;

            var presented = Encoding.ASCII.GetBytes(Hash(presentedKey));
            var stored = Encoding.ASCII.GetBytes(storedHash.ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(presented, stored);
        }

        public static string NormalizeHostname(string hostname)
        {
            var h = (hostname ?? string.Empty).Trim();
            var dot = h.IndexOf('.');
            if (dot > 0) h = h.Substring(0, dot);
            return h.ToUpperInvariant();
        }
    }
}

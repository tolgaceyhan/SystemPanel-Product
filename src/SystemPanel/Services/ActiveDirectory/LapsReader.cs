using System;
using System.Globalization;
using System.Text.Json;

namespace SystemPanel.Services.ActiveDirectory
{
    public enum LapsSource
    {
        None,
        WindowsLaps,
        WindowsLapsEncrypted,
        LegacyLaps,
    }

    /// <summary>
    /// LAPS okuma sonucu. <see cref="Password"/> ASLA loglanmaz, audit'e yazılmaz, saklanmaz
    /// ve ToString() çıktısında yer almaz.
    /// </summary>
    public sealed class LapsResult
    {
        private LapsResult(LapsSource source, string? password, string? account, DateTime? expiresUtc, DateTime? updatedUtc, string? message)
        {
            Source = source;
            Password = password;
            AccountName = account;
            ExpiresUtc = expiresUtc;
            UpdatedUtc = updatedUtc;
            Message = message;
        }

        public LapsSource Source { get; }
        public string? Password { get; }
        public string? AccountName { get; }
        public DateTime? ExpiresUtc { get; }
        public DateTime? UpdatedUtc { get; }
        public string? Message { get; }

        public bool HasPassword => !string.IsNullOrEmpty(Password);

        public static LapsResult NotFound(string message) => new(LapsSource.None, null, null, null, null, message);

        public override string ToString() => $"LapsResult(Source={Source}, HasPassword={HasPassword})";

        /// <summary>
        /// LDAP öznitelik değerlerinden sonuç üretir. Windows LAPS öncelikli; yoksa legacy LAPS.
        /// </summary>
        /// <param name="windowsLapsJson">msLAPS-Password (şifresiz modda JSON: {"n":hesap,"t":hexFileTime,"p":parola})</param>
        /// <param name="windowsLapsExpiration">msLAPS-PasswordExpirationTime (FILETIME)</param>
        /// <param name="hasEncryptedWindowsLaps">msLAPS-EncryptedPassword mevcut mu</param>
        /// <param name="legacyPassword">ms-Mcs-AdmPwd</param>
        /// <param name="legacyExpiration">ms-Mcs-AdmPwdExpirationTime (FILETIME)</param>
        public static LapsResult FromAttributes(
            string? windowsLapsJson,
            long? windowsLapsExpiration,
            bool hasEncryptedWindowsLaps,
            string? legacyPassword,
            long? legacyExpiration)
        {
            if (!string.IsNullOrWhiteSpace(windowsLapsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(windowsLapsJson);
                    var root = doc.RootElement;
                    var pwd = root.TryGetProperty("p", out var p) ? p.GetString() : null;
                    var account = root.TryGetProperty("n", out var n) ? n.GetString() : null;
                    DateTime? updated = null;
                    if (root.TryGetProperty("t", out var t) &&
                        long.TryParse(t.GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ft))
                        updated = FromFileTime(ft);

                    if (!string.IsNullOrEmpty(pwd))
                        return new LapsResult(LapsSource.WindowsLaps, pwd, account, FromFileTime(windowsLapsExpiration), updated, null);
                }
                catch (JsonException)
                {
                    return NotFound("Windows LAPS değeri ayrıştırılamadı.");
                }
            }

            if (hasEncryptedWindowsLaps)
            {
                return new LapsResult(LapsSource.WindowsLapsEncrypted, null, null, FromFileTime(windowsLapsExpiration), null,
                    "Bu bilgisayarda şifreli Windows LAPS kullanılıyor. Parola yetkili bir yönetici tarafından Get-LapsADPassword ile okunmalıdır (panel desteği ROADMAP'te).");
            }

            if (!string.IsNullOrEmpty(legacyPassword))
                return new LapsResult(LapsSource.LegacyLaps, legacyPassword, null, FromFileTime(legacyExpiration), null, null);

            return NotFound("Bu bilgisayar için LAPS parolası bulunamadı veya okuma yetkisi yok.");
        }

        private static DateTime? FromFileTime(long? fileTime)
            => fileTime is > 0 and < 2650467743999999999 ? DateTime.FromFileTimeUtc(fileTime.Value) : null;
    }
}

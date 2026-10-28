namespace SystemPanel.Configuration
{
    /// <summary>"Security" yapılandırma bölümü.</summary>
    public sealed class SecurityOptions
    {
        public const string SectionName = "Security";

        /// <summary>
        /// true (varsayılan): HTTPS yönlendirmesi + HSTS + yalnızca HTTPS üzerinden gönderilen
        /// (Secure) cookie'ler. Yalnızca geçici geçiş döneminde ve kapalı/izole ağda false yapılmalıdır;
        /// false iken oturum cookie'si ağda açık metin taşınır (bkz. docs/SECURITY.md).
        /// </summary>
        public bool RequireHttps { get; set; } = true;

        /// <summary>Oturum hareketsizlik süresi (dakika, kayan).</summary>
        public int SessionTimeoutMinutes { get; set; } = 480;

        /// <summary>Yerel (SQL) hesaplarda kilitlenmeden önce izin verilen hatalı deneme sayısı.</summary>
        public int MaxFailedAccessAttempts { get; set; } = 5;

        public int LockoutMinutes { get; set; } = 15;

        /// <summary>DataProtection anahtar klasörü. Boşsa %ProgramData%\SystemPanel\DataProtection-Keys.</summary>
        public string? DataProtectionKeysPath { get; set; }
    }
}

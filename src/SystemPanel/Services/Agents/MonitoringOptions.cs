namespace SystemPanel.Services.Agents
{
    /// <summary>"Monitoring" yapılandırma bölümü.</summary>
    public sealed class MonitoringOptions
    {
        public const string SectionName = "Monitoring";

        /// <summary>
        /// GEÇİŞ İÇİN: eski anonim /api/agentmetrics/ingest ucunu açar. Varsayılan KAPALI.
        /// Açıkken herkes sunucu kaydedip metrik gönderebilir; her istek uyarı olarak loglanır.
        /// </summary>
        public bool AllowLegacyAnonymousIngest { get; set; }

        /// <summary>Agent'a önerilen metrik gönderim aralığı.</summary>
        public int RecommendedIntervalSeconds { get; set; } = 30;

        /// <summary>Bu süre boyunca veri gelmeyen agent "çevrimdışı" sayılır.</summary>
        public int AgentOfflineAfterSeconds { get; set; } = 120;

        public ThresholdOptions Thresholds { get; set; } = new();

        public RetentionOptions Retention { get; set; } = new();
    }

    /// <summary>Basit eşik alarmları (önceki sabit değerler korunmuştur). Gelişmiş alarm motoru ROADMAP P1.</summary>
    public sealed class ThresholdOptions
    {
        public int CpuPercent { get; set; } = 85;
        public int RamPercent { get; set; } = 90;
        public int DiskPercent { get; set; } = 90;

        /// <summary>Aynı sunucu + alarm tipi için tekrar alarm üretmeden önce beklenen süre.</summary>
        public int CooldownMinutes { get; set; } = 10;
    }

    /// <summary>
    /// Metrik geçmişi saklama süresi. VERİ SİLER; bu yüzden varsayılan KAPALI ve açıkça
    /// etkinleştirilmelidir (bkz. docs/DATABASE.md "Retention").
    /// </summary>
    public sealed class RetentionOptions
    {
        public bool Enabled { get; set; }
        public int MetricHistoryDays { get; set; } = 30;
        public int AlarmDays { get; set; } = 180;
        public int RunEveryHours { get; set; } = 6;
        public int BatchSize { get; set; } = 5000;
    }
}

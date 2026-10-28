namespace SystemPanel.ViewModels.Statistics
{
    /// <summary>
    /// Ana sayfa istatistik kartları. Her değerin izlenebilir bir kaynağı vardır;
    /// kaynağı olmayan değer null'dır ve "—" gösterilir (önceden sabit sayılar gösteriliyordu).
    /// </summary>
    public class StatisticsViewModel
    {
        /// <summary>SystemPanel'e kayıtlı (Identity) kullanıcı sayısı.</summary>
        public int? TotalUsers { get; set; }

        /// <summary>Aktif oturum sayısı: oturum takibi henüz yok (ROADMAP).</summary>
        public int? ActiveSessions { get; set; }

        public string SystemHealth { get; set; } = "Unknown";
    }
}

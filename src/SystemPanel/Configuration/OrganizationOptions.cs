namespace SystemPanel.Configuration
{
    /// <summary>
    /// Kurumun organizasyon yapısının arayüzdeki adı. Teknik olarak her yerde "birim" (OU tabanlı kapsam)
    /// kullanılır; kurum bunu "İl", "Bölge", "Şube", "Kampüs" vb. olarak adlandırabilir.
    /// İç adlar (Province alanları, sp:province claim'i) geriye uyumluluk için değişmez.
    /// </summary>
    public sealed class OrganizationOptions
    {
        public const string SectionName = "Organization";

        /// <summary>Tekil etiket (ör. "Birim", "İl", "Bölge").</summary>
        public string UnitLabel { get; set; } = "Birim";

        /// <summary>Çoğul etiket (ör. "Birimler", "İller").</summary>
        public string UnitLabelPlural { get; set; } = "Birimler";

        public OrganizationMapOptions Map { get; set; } = new();
    }

    public sealed class OrganizationMapOptions
    {
        /// <summary>
        /// Türkiye il haritası modülü (harita, il ayrıntısı, il bazlı auto-heal ekranları).
        /// Yalnızca birimleri Türkiye illerine karşılık gelen kurumlar için açılır.
        /// </summary>
        public bool Enabled { get; set; }
    }
}

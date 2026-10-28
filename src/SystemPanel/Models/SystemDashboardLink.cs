namespace SystemPanel.Models
{
    public class SystemDashboardLink
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string Url { get; set; } = null!;
       /// <summary>
       /// 1=Sol Üst, 2=Sağ Üst, 3=Sol Alt, 4=Sağ Alt
       /// </summary>

        public int Position     { get; set; }

        /// <summary>
        /// 0=Auto, 1-4 Sabit Layout
        /// </summary>

        public int LayoutType { get; set; }

        /// <summary>
        /// NULL = KAPALI, Saniye cinsinden auto refresh
        /// </summary>

        public int? RefreshIntervalSec { get; set; }

        public string Screen { get; set; } = "A";
        public double? ZoomFactor { get; set; } // Zoom Ayarı Ekranlar

        public bool IsActive { get; set; }

        public  DateTime CreatedAt { get; set; }

}
}

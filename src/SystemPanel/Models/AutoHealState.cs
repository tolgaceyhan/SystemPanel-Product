using System;

namespace SystemPanel.Models
{
    public class AutoHealState
    {
        public int Id { get; set; }

        public string ProvinceName { get; set; } = "";

        // ⏱ Son deneme zamanı
        public DateTime LastAttempt { get; set; }

        // ⏱ Son BAŞARILI heal
        public DateTime? LastHealUtc { get; set; }

        // ✅ Son deneme başarılı mı
        public bool Success { get; set; }

        // ❌ Hata mesajı (mail / alarm için)
        public string? ErrorMessage { get; set; }

        // ⛔ İl bazlı auto-heal kapatma
        public bool Disabled { get; set; }
    }
}

using System;

namespace SystemPanel.Models
{
    public class Announcements
    {
        public int Id { get; set; }

        public string Turu { get; set; }       // Duyuru türü
        public string Birimi { get; set; }     // Birim
        public string Aciklama { get; set; }   // Kısa açıklama / başlık
        public string Detay { get; set; }      // Detaylı metin

        public string? FilePath { get; set; }  // Yüklenen dosya (pdf/resim) yolu

        public DateTime KayitTarihi { get; set; } = DateTime.Now;
    }
}

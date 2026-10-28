using Microsoft.AspNetCore.Mvc;

namespace SystemPanel.ViewModels.Announcements
{
    public class AnnouncementsViewModel
    {
        public int Id { get; set; }
        public string Turu { get; set; }
        public string Birimi { get; set; }
        public string Baslik { get; set; }
        public DateTime KayitTarihi { get; set; }
    }

}




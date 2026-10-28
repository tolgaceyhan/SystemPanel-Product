using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SystemPanel.ViewModels.Announcements
{
    public class CreateAnnouncementViewModel
    {
        [Required(ErrorMessage = "Türü alanı zorunludur.")]
        [Display(Name = "Türü")]
        public string Turu { get; set; }

        [Required(ErrorMessage = "Birim alanı zorunludur.")]
        [Display(Name = "Birim")]
        public string Birimi { get; set; }

        [Required(ErrorMessage = "Açıklama alanı zorunludur.")]
        [Display(Name = "Açıklama")]
        public string Aciklama { get; set; }

        [Required(ErrorMessage = "Detay alanı zorunludur.")]
        [Display(Name = "Detay")]
        public string Detay { get; set; }

        [Display(Name = "Ek Dosya (PDF/Resim)")]
        public IFormFile? File { get; set; }
    }
}

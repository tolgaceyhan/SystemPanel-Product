using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.Gpo
{
    public class CreateGpoViewModel
    {
        [Required]
        [Display(Name = "GPO Adı")]
        public string Name { get; set; }

        [Display(Name = "Açıklama")]
        public string Description { get; set; }

        // Buraya OU distinguishedName gelecek:
        // Ör: OU=<Birim>,OU=<Bilgisayarlar>,DC=example,DC=local
        [Required]
        [Display(Name = "Organizasyon Birimi (OU DN)")]
        public string OrganizationalUnit { get; set; }

        [Display(Name = "Script / Template Dosyası")]
        public IFormFile PolicyScriptFile { get; set; }

        [Display(Name = "Etkin mi?")]
        public bool IsEnabled { get; set; } = true;
    }
}

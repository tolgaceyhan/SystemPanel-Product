using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SystemPanel.ViewModels.AdUser
{
    public class CreateAdUserViewModel
    {
        [Required]
        [Display(Name = "Kullanıcı Adı (sAMAccountName)")]
        [StringLength(64)]
        public string UserName { get; set; }

        [Required]
        [Display(Name = "Görünen Ad (DisplayName)")]
        [StringLength(128)]
        public string DisplayName { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "E-posta")]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Parola")]
        [StringLength(128, MinimumLength = 8)]
        public string Password { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Parola (Tekrar)")]
        [Compare("Password", ErrorMessage = "Parolalar uyuşmuyor.")]
        public string ConfirmPassword { get; set; }

        [Required]
        [Display(Name = "Hedef OU (DN)")]
        public string SelectedOu { get; set; }

        /// <summary>
        /// Dropdown için OU listesi
        /// </summary>
        public IEnumerable<SelectListItem> AvailableOus { get; set; } = new List<SelectListItem>();
    }
}

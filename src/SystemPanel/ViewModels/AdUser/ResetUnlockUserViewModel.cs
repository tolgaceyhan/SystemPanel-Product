using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.AdUser
{
    public class ResetUnlockUserViewModel
    {
        [Required]
        public string UserDn { get; set; }

        public string UserName { get; set; }
        public string DisplayName { get; set; }

        [Display(Name = "Yeni Şifre")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Şifre en az 8 karakter olmalıdır.")]
        public string NewPassword { get; set; }

        public bool IsLocked { get; set; }

        // Kilitli hesaplar için bu alan true olur
        public bool ShowUnlockOption => IsLocked;
    }
}

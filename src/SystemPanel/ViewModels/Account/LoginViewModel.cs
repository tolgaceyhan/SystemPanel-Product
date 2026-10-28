using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.Account
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
        [Display(Name = "Kullanıcı Adı")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        [Display(Name = "Parola")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Beni Hatırla")]
        public bool RememberMe { get; set; }

        // Eğer ileride AD/SQL seçimi yaptırmak istersen kullanılabilir
        public bool IsAdUser { get; set; }
    }
}

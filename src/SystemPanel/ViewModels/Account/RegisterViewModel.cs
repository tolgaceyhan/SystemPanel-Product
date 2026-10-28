using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.Account
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
        [StringLength(50, ErrorMessage = "Kullanıcı adı en fazla 50 karakter olabilir.")]
        [RegularExpression(@"^[a-zA-Z0-9]*$", ErrorMessage = "Kullanıcı adı yalnızca harf ve rakamlardan oluşabilir.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "E-posta adresi zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [StringLength(100, ErrorMessage = "Şifre en az {2} ve en fazla {1} karakter olmalıdır.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*])[A-Za-z\d@$!%*?&]{6,}$", ErrorMessage = "Şifre en az bir büyük harf, bir rakam ve bir özel karakter içermelidir.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Şifre doğrulama zorunludur.")]
        [Compare("Password", ErrorMessage = "Şifre ve şifre doğrulama eşleşmiyor.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; }
    }
}

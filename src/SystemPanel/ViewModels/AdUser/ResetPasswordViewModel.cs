using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.AdUser
{
    public class ResetPasswordViewModel
    {
        [Required]
        public string UserName { get; set; }

        [Required(ErrorMessage = "Yeni parola zorunludur.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Parola tekrarı zorunludur.")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Parolalar uyuşmuyor.")]
        public string ConfirmPassword { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.UserManagement
{
    public class CreateUserViewModel
    {
        [Required]
        public string UserName { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; }

        public string PhoneNumber { get; set; }

        [Required]
        [MinLength(6)]
        public string Password { get; set; }

        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; }

        public string Role { get; set; }
    }
}

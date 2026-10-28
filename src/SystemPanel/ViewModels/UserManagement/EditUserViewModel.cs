using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.UserManagement
{
    public class EditUserViewModel
    {
        public string Id { get; set; }

        [Required]
        public string UserName { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; }

        public string PhoneNumber { get; set; }

        public bool LockoutEnabled { get; set; }
        public bool IsLockedOut { get; set; }

        public IList<string> AllRoles { get; set; } = new List<string>();
        public IList<string> Roles { get; set; } = new List<string>();
        public IList<string> SelectedRoles { get; set; } = new List<string>();
    }
}

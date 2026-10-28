using System;
using System.Collections.Generic;

namespace SystemPanel.ViewModels.UserManagement
{
    public class UserDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; }
        public bool LockoutEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }

        public IEnumerable<string> Roles { get; set; } = Array.Empty<string>();
    }
}

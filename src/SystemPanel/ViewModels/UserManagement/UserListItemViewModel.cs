using System;
using System.Collections.Generic;

namespace SystemPanel.ViewModels.UserManagement
{
    public class UserListItemViewModel
    {
        public string Id { get; set; }

        public string UserName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; }

        public bool LockoutEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }

        public IList<string> Roles { get; set; } = new List<string>();

        public bool IsLocked =>
            LockoutEnd.HasValue && LockoutEnd.Value > DateTimeOffset.UtcNow;
    }
}

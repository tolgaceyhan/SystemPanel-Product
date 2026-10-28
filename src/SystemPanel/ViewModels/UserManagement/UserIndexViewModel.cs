using System.Collections.Generic;

namespace SystemPanel.ViewModels.UserManagement
{
    public class UserIndexViewModel
    {
        public string? Search { get; set; }
        public List<UserListItemViewModel> Users { get; set; } = new();
    }
}

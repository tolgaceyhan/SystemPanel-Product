using System.Collections.Generic;

namespace SystemPanel.ViewModels.UserManagement
{
    public class UserListViewModel
    {
        public string Search { get; set; } = "";
        public string Sort { get; set; } = "UserName";
        public string Dir { get; set; } = "asc";

        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalItems { get; set; }
        public int TotalPages =>
            (TotalItems + PageSize - 1) / PageSize;

        public List<UserListItemViewModel> Users { get; set; } = new();
    }
}

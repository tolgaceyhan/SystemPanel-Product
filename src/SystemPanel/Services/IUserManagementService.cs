using System.Threading.Tasks;
using SystemPanel.ViewModels.UserManagement;

namespace SystemPanel.Services
{
    public interface IUserManagementService
    {
        Task<UserListViewModel> GetPagedUsersAsync(
            int page,
            string search,
            string sort,
            string dir,
            int pageSize = 20);
    }
}

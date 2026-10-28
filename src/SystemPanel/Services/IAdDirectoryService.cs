// Dosya: Services/IAdDirectoryService.cs
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SystemPanel.Services
{
    public interface IAdDirectoryService
    {
        Task<List<AdUserDto>> GetLocalAdminsAsync();
        Task<List<AdUserDto>> GetSubeMudurlariAsync();
        Task<List<AdUserDto>> GetAllUsersAsync();
        Task<List<AdUserDto>> GetUsersByOuAsync(string ouDn);
        Task<AdUserDto?> GetUserBySicilAsync(string sicil);
        Task<List<AdUserDto>> GetUsersByDnsAsync(IEnumerable<string> dns);
    }
}

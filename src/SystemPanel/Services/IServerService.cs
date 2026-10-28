using System.Collections.Generic;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public interface IServerService
    {
        Task<IEnumerable<ServerStatus>> GetAllServersAsync();
        Task<ServerStatus> GetServerByIdAsync(int id);
        Task UpdateServerStatusAsync(int id, string status);
    }
}

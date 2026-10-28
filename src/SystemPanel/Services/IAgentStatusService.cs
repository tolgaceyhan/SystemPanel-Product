using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public interface IAgentStatusService
    {
        Task<AgentStateResult> GetStatusAsync(string serverName);
    }
}

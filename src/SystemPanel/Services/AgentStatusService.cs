using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class AgentStatusService : IAgentStatusService
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeSpan _offlineAfter;

        public AgentStatusService(ApplicationDbContext db, Microsoft.Extensions.Options.IOptions<SystemPanel.Services.Agents.MonitoringOptions> options)
        {
            _db = db;
            _offlineAfter = TimeSpan.FromSeconds(Math.Clamp(options.Value.AgentOfflineAfterSeconds, 30, 86400));
        }

        public async Task<AgentStateResult> GetStatusAsync(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return new AgentStateResult
                {
                    AgentInstalled = false,
                    AgentOnline = false
                };
            }

            var server = await _db.ServerInfos
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == serverName);

            if (server == null)
            {
                return new AgentStateResult
                {
                    AgentInstalled = false,
                    AgentOnline = false
                };
            }

            // Agent kurulu kabul etme kriteri:
            // ServerInfos satırı var + LastSeenAt doluysa agent en az 1 kez veri göndermiştir.
            if (server.LastSeenAt == null)
            {
                return new AgentStateResult
                {
                    AgentInstalled = true,
                    AgentOnline = false,
                    LastSeenAt = null
                };
            }

            var diff = DateTime.UtcNow - server.LastSeenAt.Value;

            return new AgentStateResult
            {
                AgentInstalled = true,
                AgentOnline = diff <= _offlineAfter,
                LastSeenAt = server.LastSeenAt
            };
        }
    }
}

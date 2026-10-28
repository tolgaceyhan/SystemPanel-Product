using System;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class ServerLogService
    {
        private readonly ApplicationDbContext _context;

        public ServerLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public void Log(string action, string serverName, string performedBy, string? details = null)
        {
            var entry = new ServerLog
            {
                Action = action,
                ServerName = serverName,
                PerformedBy = performedBy,
                Details = details,
                Timestamp = DateTime.Now
            };

            _context.ServerLogs.Add(entry);
            _context.SaveChanges();
        }
    }
}

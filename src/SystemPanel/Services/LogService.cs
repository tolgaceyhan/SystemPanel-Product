using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.ViewModels.Logs;

namespace SystemPanel.Services
{
    public class LogService : ILogService
    {
        private readonly ApplicationDbContext _context;

        public LogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddLogAsync(LogEntry log)
        {
            _context.LogEntries.Add(log);
            await _context.SaveChangesAsync();
        }


        public async Task LogAsync(string level, string message, string details, string user, string ip)
        {
            var log = new LogEntry
            {
                Level = level,
                Message = message,
                Details = details,
                User = user,
                IP = ip,
                Timestamp = DateTime.Now
            };

            _context.LogEntries.Add(log);
            await _context.SaveChangesAsync();
        }


        public async Task<IEnumerable<LogEntry>> GetFilteredLogsAsync(FilterLogsViewModel filter)
        {
            var query = _context.LogEntries.AsQueryable();

            if (!string.IsNullOrEmpty(filter.MessageContains))
                query = query.Where(l => l.Message.Contains(filter.MessageContains));

            if (!string.IsNullOrEmpty(filter.Level))
                query = query.Where(l => l.Level == filter.Level);

            if (!string.IsNullOrEmpty(filter.User))
                query = query.Where(l => l.User == filter.User);

            if (filter.FromDate.HasValue)
                query = query.Where(l => l.Timestamp >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(l => l.Timestamp <= filter.ToDate.Value);

            return await query.OrderByDescending(l => l.Timestamp).ToListAsync();
        }


        public async Task<IEnumerable<LogEntry>> GetAllLogsAsync()
        {
            return await _context.LogEntries.OrderByDescending(l => l.Timestamp).ToListAsync();
        }

        public async Task<LogEntry> GetLogByIdAsync(int id)
        {
            return await _context.LogEntries.FindAsync(id);
        }

        public async Task<IEnumerable<LogEntry>> GetLogsByMessageFilterAsync(string filter)
        {
            return await _context.LogEntries
                .Where(log => log.Message.Contains(filter))
                .ToListAsync();
        }

        public async Task<IEnumerable<LogEntry>> GetLogsByCriteriaAsync(string user, string ip, DateTime date)
        {
            return await _context.LogEntries
                .Where(log =>
                    (log.User == user || log.IP == ip) &&
                    log.Timestamp.Date == date.Date)
                .ToListAsync();
        }

        public async Task<int> ClearLogsAsync(string? logType, DateTime? beforeDate)
        {
            var query = _context.LogEntries.AsQueryable();

            if (!string.IsNullOrWhiteSpace(logType) && logType != "All")
                query = query.Where(l => l.Level == logType);

            if (beforeDate.HasValue)
                query = query.Where(l => l.Timestamp < beforeDate.Value);

            var toDelete = await query.ToListAsync();

            _context.LogEntries.RemoveRange(toDelete);
            await _context.SaveChangesAsync();

            return toDelete.Count;

        }

    }
}

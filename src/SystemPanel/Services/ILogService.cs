using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SystemPanel.Models;
using SystemPanel.ViewModels.Logs;

namespace SystemPanel.Services
{
    public interface ILogService
    {
        Task AddLogAsync(LogEntry log);
        Task LogAsync(string level, string message, string details, string user, string ip);
        Task<IEnumerable<LogEntry>> GetAllLogsAsync();
        Task<LogEntry> GetLogByIdAsync(int id);
        Task<IEnumerable<LogEntry>> GetLogsByCriteriaAsync(string user, string ip, DateTime date);
        Task<IEnumerable<LogEntry>> GetLogsByMessageFilterAsync(string filter);
        Task<IEnumerable<LogEntry>> GetFilteredLogsAsync(FilterLogsViewModel filter);
        Task<int> ClearLogsAsync(string logType, DateTime? beforeDate);

    }
}

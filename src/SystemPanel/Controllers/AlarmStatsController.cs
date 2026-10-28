using SystemPanel.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.ViewModels.Alarms;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.MonitoringView)]
    public class AlarmsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AlarmsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // -------------------------------
        // ALARM FİLTRELEME
        // -------------------------------
        [HttpGet]
        public async Task<IActionResult> Filter(string? serverName, string? type, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.AlarmLogs.AsQueryable();

            // ---- FİLTRELER ----
            if (!string.IsNullOrEmpty(serverName))
                query = query.Where(a => a.ServerName.Contains(serverName));

            if (!string.IsNullOrEmpty(type))
                query = query.Where(a => a.AlarmType.StartsWith(type));

            if (startDate.HasValue)
                query = query.Where(a => a.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(a => a.CreatedAt <= endDate.Value);

            // ---- DİNAMİK ALARM TİPLERİ (CPU, RAM, Disk C:, Disk D:...) ----
            var alarmTypes = await _context.AlarmLogs
                .Select(a => a.AlarmType)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.AlarmTypes = alarmTypes;

            // ---- MODEL ----
            var model = new AlarmFilterViewModel
            {
                ServerName = serverName,
                Type = type,
                StartDate = startDate,
                EndDate = endDate,
                Results = await query.OrderByDescending(a => a.CreatedAt).ToListAsync()
            };

            return View("Filtered", model);
        }


        // -------------------------------
        // ALARM ÖZETİ / DASHBOARD
        // -------------------------------
        public async Task<IActionResult> Index()
        {
            var alarms = await _context.AlarmLogs
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var model = new AlarmStatsViewModel
            {
                TotalAlarms = alarms.Count,
                CpuAlarms = alarms.Count(a => a.AlarmType == "CPU"),
                RamAlarms = alarms.Count(a => a.AlarmType == "RAM"),
                DiskAlarms = alarms.Count(a => a.AlarmType.StartsWith("Disk")),
                Last10Alarms = alarms.Take(10).ToList()
            };

            return View(model);
        }
    }
}

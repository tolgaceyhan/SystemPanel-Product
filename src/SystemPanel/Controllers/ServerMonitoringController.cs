using SystemPanel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Services;
using SystemPanel.ViewModels.Server;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.ServersView)]
    public class ServerMonitoringController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ServerMonitoringController> _logger;
        private readonly IAdComputerService _adComputerService;
        private readonly IAgentStatusService _agentStatusService;

        public ServerMonitoringController(
            ApplicationDbContext db,
            ILogger<ServerMonitoringController> logger,
            IAdComputerService adComputerService,
            IAgentStatusService agentStatusService)
        {
            _db = db;
            _logger = logger;
            _adComputerService = adComputerService;
            _agentStatusService = agentStatusService;
        }

        // =========================
        // MANUAL SERVER ADD
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.ServersManage)]
        public async Task<IActionResult> AddServer(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                return RedirectToAction(nameof(Index));

            bool exists = await _db.ServerInfos.AnyAsync(s => s.Name == serverName);
            if (!exists)
            {
                _db.ServerInfos.Add(new ServerInfo
                {
                    Name = serverName,
                    IsActive = true,
                    Type = "Server",
                    Description = "Manual add",
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // SERVER LIST / DASHBOARD
        // =========================
        public async Task<IActionResult> Index()
        {
            var servers = await _db.ServerInfos
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();

            var latestMetrics = await _db.LatestServerMetrics
                .AsNoTracking()
                .ToDictionaryAsync(x => x.ServerName);

            var model = new List<ServerSummaryViewModel>();

            foreach (var s in servers)
            {
                latestMetrics.TryGetValue(s.Name, out var latest);

                var disks = await _db.ServerDiskHistory
                    .AsNoTracking()
                    .Where(x => x.ServerName.ToUpper() == s.Name.ToUpper())
                    .OrderByDescending(x => x.CreatedAt)
                    .GroupBy(x => x.DriveName)
                    .ToDictionaryAsync(g => g.Key, g => g.First().Usage);

                var agentState = await _agentStatusService.GetStatusAsync(s.Name);

                model.Add(new ServerSummaryViewModel
                {
                    ServerName = s.Name,
                    Description = s.Description,
                    LastUpdated = agentState.LastSeenAt,

                    CpuUsage = latest?.CpuUsage ?? 0,
                    RamUsage = latest?.RamUsage ?? 0,
                    DiskUsages = disks,

                    Status = !agentState.AgentInstalled
                        ? "Agent Yok"
                        : agentState.AgentOnline ? "Online" : "Offline"
                });
            }

            return View(model);
        }

        // =========================
        // AD SERVER DISCOVERY
        // =========================
        [HttpGet]
        public async Task<IActionResult> DiscoverServers(string? ou, string? ipPrefix)
        {
            var model = await _adComputerService.DiscoverServersAsync(ou, ipPrefix);

            // ✅ Keşif ekranına AgentInstalled/AgentOnline bilgisini bağla
            foreach (var s in model)
            {
                var state = await _agentStatusService.GetStatusAsync(s.Name);
                s.AgentInstalled = state.AgentInstalled;
                s.AgentOnline = state.AgentOnline;
                s.LastSeenAt = state.LastSeenAt;
            }

            return View(model);
        }

        // =========================
        // SERVER DETAILS
        // =========================
        public async Task<IActionResult> Details(string id, string range = "1h")
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction(nameof(Index));

            var server = await _db.ServerInfos
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == id);

            if (server == null)
                return NotFound();

            AdComputer? adInfo = null;
            try
            {
                adInfo = await _adComputerService.GetComputerByNameAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AD lookup failed for {Server}", id);
            }

            DateTime since = range switch
            {
                "1h" => DateTime.UtcNow.AddHours(-1),
                "6h" => DateTime.UtcNow.AddHours(-6),
                "24h" => DateTime.UtcNow.AddHours(-24),
                _ => DateTime.UtcNow.AddHours(-1)
            };

            // ⚠️ cpu/ram history
            var metrics = await _db.ServerMetricHistory
                .AsNoTracking()
                .Where(x => x.ServerName == id &&
                            x.CreatedAt >= since &&
                            (x.MetricType == "cpu" || x.MetricType == "ram"))
                .OrderBy(x => x.CreatedAt)
                .Take(300)
                .ToListAsync();

            // ⚠️ disk history (senin view disk'i ServerDiskHistory'den okuyorsa burada kalabilir)
            var diskRows = await _db.ServerDiskHistory
                .AsNoTracking()
                .Where(x => x.ServerName == id && x.CreatedAt >= since)
                .OrderBy(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            var diskHistory = diskRows
                .GroupBy(x => x.DriveName)
                .ToDictionary(g => g.Key, g => g.ToList());

            var latest = await _db.LatestServerMetrics
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ServerName == id);

            return View(new ServerDetailViewModel
            {
                Name = server.Name,
                Description = adInfo?.Description ?? server.Description,
                OperatingSystem = adInfo?.OperatingSystem,
                DistinguishedName = adInfo?.DistinguishedName,
                LastLogonDate = adInfo?.LastLogonDate,

                CpuUsage = latest?.CpuUsage ?? 0,
                RamUsage = latest?.RamUsage ?? 0,

                MetricHistory = metrics,
                DiskHistory = diskHistory
            });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using System.Text;
using SystemPanel.Auditing;
using SystemPanel.Security;
using SystemPanel.Services;
using SystemPanel.ViewModels.Logs;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using SystemPanel.Models;
using SystemPanel.ViewModels;
using System;
using System.Collections.Generic;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.LogsView)]
    public class LogsController : Controller
    {
        private readonly ILogService _logService;
        private readonly IAuditService _audit;

        public LogsController(ILogService logService, IAuditService audit)
        {
            _logService = logService;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index(FilterLogsViewModel filter)
        {
            var logs = await _logService.GetFilteredLogsAsync(filter);
            filter.Logs = logs.ToList();
            return View(filter);
        }

        [HttpGet]
        public async Task<IActionResult> Search(string query)
        {
            var results = await _logService.GetLogsByMessageFilterAsync(query ?? string.Empty);
            return View("Index", new FilterLogsViewModel
            {
                MessageContains = query,
                Logs = results.ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var log = await _logService.GetLogByIdAsync(id);
            if (log == null) return NotFound();

            var viewModel = new LogDetailViewModel
            {
                Id = log.Id,
                Level = log.Level,
                Message = log.Message,
                Timestamp = log.Timestamp,
                Details = log.Details,
                User = log.User,
                IP = log.IP
            };
            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> RelatedLogs(int id)
        {
            var currentLog = await _logService.GetLogByIdAsync(id);
            if (currentLog == null) return NotFound();

            var relatedLogs = await _logService.GetLogsByCriteriaAsync(currentLog.User, currentLog.IP, currentLog.Timestamp);

            var viewModel = new RelatedLogsViewModel
            {
                CurrentLog = currentLog,
                RelatedLogs = relatedLogs
            };
            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ExportToPdf()
        {
            var logs = await _logService.GetAllLogsAsync();

            var pdf = SystemPanel.Services.Export.SimplePdfWriter.Write(
                "Log Listesi",
                logs.Select(log => $"[{log.Timestamp}] {log.Level} - {log.Message}"));

            return File(pdf, "application/pdf", "logs.pdf");
        }

        [HttpGet]
        public async Task<IActionResult> ExportToCsv()
        {
            var logs = await _logService.GetAllLogsAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ID,Timestamp,Level,Message,Details,User,IP");

            // Basit CSV kaçışları (çift tırnak içi, içte çift tırnak -> iki çift tırnak)
            string Esc(string? s)
            {
                s ??= "";
                s = s.Replace("\"", "\"\"");
                return $"\"{s}\"";
            }

            foreach (var log in logs)
                sb.AppendLine($"{log.Id},{Esc(log.Timestamp.ToString())},{Esc(log.Level)},{Esc(log.Message)},{Esc(log.Details)},{Esc(log.User)},{Esc(log.IP)}");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "logs.csv");
        }

        [HttpGet]
        public async Task<IActionResult> AuditTrail()
        {
            // Basit audit: kullanıcı aksiyonlarını listele
            var all = await _logService.GetAllLogsAsync();
            var onlyUser = all
                .Where(l => l.Level == "Info" || l.Level == "Warning" || l.Level == "Error")
                .OrderByDescending(l => l.Timestamp)
                .Select(l => new AuditEntryViewModel
                {
                    Timestamp = l.Timestamp,
                    User = l.User ?? "",
                    Action = l.Message ?? "",
                    Detail = l.Details ?? "",
                    Ip = l.IP ?? ""
                })
                .ToList();

            return View(onlyUser); // Views/Logs/AuditTrail.cshtml @model IEnumerable<AuditEntryViewModel>
        }

        [HttpGet]
        [RequirePermission(Permissions.LogsManage)]
        public IActionResult ClearLogs()
        {
            return View(new ClearLogsViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.LogsManage)]
        public async Task<IActionResult> ClearLogs(ClearLogsViewModel model)
        {
            if (!model.Confirm)
            {
                TempData["Error"] = "Lütfen işlemi onaylayınız.";
                return View(model);
            }

            int deletedCount = await _logService.ClearLogsAsync(model.LogType ?? "All", model.BeforeDate);

            await _audit.RecordAsync(new AuditRecord(AuditActions.LogsClear)
            {
                ResourceType = "LogEntries",
                Metadata = new { model.LogType, model.BeforeDate, DeletedCount = deletedCount },
            });

            TempData["Message"] = $"{deletedCount} log kaydı başarıyla silindi.";

            await _logService.LogAsync(
                "Info",
                "Log Temizleme",
                $"{deletedCount} kayıt silindi. Tür: {model.LogType}, Tarih: {model.BeforeDate?.ToShortDateString() ?? "Yok"}",
                User.Identity?.Name ?? "anonim",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "IP yok"
            );

            return RedirectToAction(nameof(Index));
        }
    }
}

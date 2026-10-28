using SystemPanel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using SystemPanel.Data;
using Microsoft.EntityFrameworkCore;

[RequirePermission(Permissions.MonitoringManage)]
public class AlarmExportController : Controller
{
    private readonly ApplicationDbContext _context;

    public AlarmExportController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> ExportToCsv()
    {
        var alarms = await _context.AlarmLogs
            .OrderByDescending(a => a.CreatedAt)   // Timestamp → CreatedAt
            .ToListAsync();

        var builder = new StringBuilder();

        // Yeni AlarmLog yapısına göre CSV başlıkları
        builder.AppendLine("ServerName,AlarmType,Message,CreatedAt");

        foreach (var a in alarms)
        {
            builder.AppendLine(
                $"{a.ServerName}," +
                $"{a.AlarmType}," +
                $"\"{a.Message}\"," +
                $"{a.CreatedAt:yyyy-MM-dd HH:mm}"
            );
        }

        return File(
            Encoding.UTF8.GetBytes(builder.ToString()),
            "text/csv",
            "AlarmLogs.csv"
        );
    }
}

using SystemPanel.Security;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Controllers.Api
{
    // Önceden anonimdi (herkes alarm ekleyebiliyordu). Agent alarmları ingest üzerinden üretilir.
    [ApiController]
    [Route("api/alarms")]
    [RequirePermission(Permissions.MonitoringManage)]
    public class AlarmLogController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AlarmLogController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] AlarmLog alarm)
        {
            if (alarm == null || string.IsNullOrWhiteSpace(alarm.ServerName))
                return BadRequest("Geçersiz alarm verisi.");

            // Yeni modelde Timestamp yerine CreatedAt var
            alarm.CreatedAt = DateTime.UtcNow;

            _context.AlarmLogs.Add(alarm);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Alarm başarıyla kaydedildi." });
        }
    }
}

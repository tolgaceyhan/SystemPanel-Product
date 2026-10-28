using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Auditing;
using SystemPanel.Data;
using SystemPanel.Models.Dto;
using SystemPanel.Security;

/// <summary>
/// SystemPanel.Dashboard (WPF/NOC) API'si.
/// <c>GET config</c> NOC ekranlarının oturum açmadan okuyabilmesi için bilinçli olarak anonimdir:
/// yalnızca aktif panellerin başlık/URL/yerleşim bilgisini döndürür, değişiklik yapmaz.
/// Değiştiren uçlar dashboard.manage gerektirir.
/// </summary>
[ApiController]
public class SystemDashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _audit;

    public SystemDashboardController(ApplicationDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    // ==================================================
    // GET CONFIG (Dashboard.exe bunu okur)
    //   Eski yol: /api/systemdashboard/config?screen=A
    //   v1 yolu : /api/v1/dashboard/screens/A
    // ==================================================
    [HttpGet("api/systemdashboard/config")]
    [AllowAnonymous]
    public Task<IActionResult> GetDashboardConfig([FromQuery] string screen, CancellationToken ct)
        => GetScreenConfig(screen, ct);

    [HttpGet("api/v1/dashboard/screens/{screen}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetScreenConfig(string screen, CancellationToken ct)
    {
        if (!DashboardPanelRules.IsValidScreenCode(screen))
            return Problem(statusCode: 400, title: "Geçersiz ekran kodu.");

        var panels = await _context.SystemDashboardLinks
            .AsNoTracking()
            .Where(x => x.IsActive && x.Screen == screen)
            .OrderBy(x => x.Position)
            .ToListAsync(ct);

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            Screen = screen,
            LayoutType = 0,
            GeneratedAtUtc = DateTime.UtcNow,
            Panels = panels
                .Where(x => DashboardPanelRules.IsAllowedUrl(x.Url))
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Url,
                    x.Position,
                    x.RefreshIntervalSec,
                    x.ZoomFactor
                })
        });
    }

    // ==================================================
    // REORDER (Edit Mode – Drag & Drop)
    // ==================================================
    [HttpPost("api/systemdashboard/reorder")]
    [ValidateAntiForgeryToken]
    [RequirePermission(Permissions.DashboardManage)]
    public async Task<IActionResult> Reorder(
        [FromQuery] string screen,
        [FromBody] List<DashboardOrderDto> order,
        CancellationToken ct)
    {
        if (!DashboardPanelRules.IsValidScreenCode(screen))
            return Problem(statusCode: 400, title: "screen parametresi zorunlu");

        if (order == null || order.Count == 0 || order.Any(o => o.Position is < 1 or > 4))
            return Problem(statusCode: 400, title: "Geçersiz sıralama.");

        var ids = order.Select(x => x.Id).ToList();

        var panels = await _context.SystemDashboardLinks
            .Where(x => x.Screen == screen && ids.Contains(x.Id))
            .ToListAsync(ct);

        foreach (var panel in panels)
            panel.Position = order.First(o => o.Id == panel.Id).Position;

        await _context.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditRecord(AuditActions.DashboardPanelReorder)
        {
            ResourceType = "DashboardScreen",
            ResourceId = screen,
            After = order,
        }, ct);

        return Ok();
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;

namespace SystemPanel.Controllers
{
    /// <summary>Güvenlik denetim kayıtları (yalnızca okuma; kayıtlar uygulamadan silinemez).</summary>
    [RequirePermission(Permissions.AuditView)]
    public class AuditController : Controller
    {
        private const int PageSize = 50;
        private readonly ApplicationDbContext _db;

        public AuditController(ApplicationDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Index(AuditFilter filter, CancellationToken ct)
        {
            var q = _db.AuditEvents.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Action))
                q = q.Where(a => a.Action.StartsWith(filter.Action));
            if (!string.IsNullOrWhiteSpace(filter.Actor))
                q = q.Where(a => a.ActorUserName != null && a.ActorUserName.Contains(filter.Actor));
            if (!string.IsNullOrWhiteSpace(filter.Resource))
                q = q.Where(a => (a.ResourceId != null && a.ResourceId.Contains(filter.Resource)) ||
                                 (a.ResourceName != null && a.ResourceName.Contains(filter.Resource)));
            if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
                q = q.Where(a => a.CorrelationId == filter.CorrelationId);
            if (filter.Success.HasValue)
                q = q.Where(a => a.Success == filter.Success.Value);
            if (filter.FromUtc.HasValue)
                q = q.Where(a => a.TimestampUtc >= filter.FromUtc.Value);
            if (filter.ToUtc.HasValue)
                q = q.Where(a => a.TimestampUtc < filter.ToUtc.Value.AddDays(1));

            filter.Page = Math.Max(1, filter.Page);
            filter.Total = await q.CountAsync(ct);
            filter.Items = await q.OrderByDescending(a => a.TimestampUtc)
                .Skip((filter.Page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync(ct);
            filter.PageSize = PageSize;

            return View(filter);
        }

        [HttpGet]
        public async Task<IActionResult> Details(long id, CancellationToken ct)
        {
            var e = await _db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            return e == null ? NotFound() : View(e);
        }
    }

    public class AuditFilter
    {
        public string? Action { get; set; }
        public string? Actor { get; set; }
        public string? Resource { get; set; }
        public string? CorrelationId { get; set; }
        public bool? Success { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int Page { get; set; } = 1;

        public int PageSize { get; set; }
        public int Total { get; set; }
        public System.Collections.Generic.List<AuditEvent> Items { get; set; } = new();
        public int PageCount => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
    }
}

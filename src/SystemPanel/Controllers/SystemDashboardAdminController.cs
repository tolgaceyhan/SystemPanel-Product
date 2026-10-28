using Microsoft.AspNetCore.Mvc;
using SystemPanel.Auditing;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;

/// <summary>NOC dashboard panel yönetimi. Önceden [AllowAnonymous] idi ve GET ile silme yapılabiliyordu.</summary>
[RequirePermission(Permissions.DashboardManage)]
public class SystemDashboardAdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _audit;

    public SystemDashboardAdminController(ApplicationDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    public IActionResult Index()
    {
        var list = _context.SystemDashboardLinks
            .OrderBy(x => x.Screen)
            .ThenBy(x => x.Position)
            .ToList();

        return View(list);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SystemDashboardLink model)
    {
        Normalize(model);
        ValidatePanel(model, existingId: null);

        if (!ModelState.IsValid)
            return View(model);

        model.CreatedAt = DateTime.Now;
        _context.SystemDashboardLinks.Add(model);
        await _context.SaveChangesAsync();

        await _audit.RecordAsync(new AuditRecord(AuditActions.DashboardPanelCreate)
        {
            ResourceType = "DashboardPanel",
            ResourceId = model.Id.ToString(),
            ResourceName = model.Title,
            After = Snapshot(model),
        });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var item = _context.SystemDashboardLinks.FirstOrDefault(x => x.Id == id);
        if (item != null)
        {
            _context.SystemDashboardLinks.Remove(item);
            await _context.SaveChangesAsync();

            await _audit.RecordAsync(new AuditRecord(AuditActions.DashboardPanelDelete)
            {
                ResourceType = "DashboardPanel",
                ResourceId = id.ToString(),
                ResourceName = item.Title,
                Before = Snapshot(item),
            });
        }

        return RedirectToAction(nameof(Index));
    }

    public IActionResult Edit(int id)
    {
        var item = _context.SystemDashboardLinks.Find(id);
        if (item == null)
            return NotFound();

        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SystemDashboardLink model)
    {
        Normalize(model);

        var entity = _context.SystemDashboardLinks.FirstOrDefault(x => x.Id == model.Id);
        if (entity == null)
            return NotFound();

        ValidatePanel(model, existingId: model.Id);
        if (!ModelState.IsValid)
            return View(model);

        var before = Snapshot(entity);

        entity.Title = model.Title;
        entity.Url = model.Url;
        entity.Position = model.Position;
        entity.Screen = model.Screen;
        entity.RefreshIntervalSec = model.RefreshIntervalSec;
        entity.ZoomFactor = model.ZoomFactor;
        entity.IsActive = model.IsActive;

        await _context.SaveChangesAsync();

        await _audit.RecordAsync(new AuditRecord(AuditActions.DashboardPanelUpdate)
        {
            ResourceType = "DashboardPanel",
            ResourceId = entity.Id.ToString(),
            ResourceName = entity.Title,
            Before = before,
            After = Snapshot(entity),
        });

        return RedirectToAction(nameof(Index));
    }

    private static void Normalize(SystemDashboardLink model)
    {
        model.Screen = string.IsNullOrWhiteSpace(model.Screen) ? "A" : model.Screen.Trim();
        model.Title = model.Title?.Trim() ?? string.Empty;
        model.Url = model.Url?.Trim() ?? string.Empty;
    }

    private void ValidatePanel(SystemDashboardLink model, int? existingId)
    {
        // Dashboard panelleri WebView2 içinde açılır: yalnızca http/https (file:, javascript: vb. yasak).
        if (!DashboardPanelRules.IsAllowedUrl(model.Url))
            ModelState.AddModelError(nameof(model.Url), "URL http:// veya https:// ile başlayan geçerli bir adres olmalıdır.");

        if (!DashboardPanelRules.IsValidScreenCode(model.Screen))
            ModelState.AddModelError(nameof(model.Screen), "Ekran kodu yalnızca harf, rakam, '-' ve '_' içerebilir (en fazla 32 karakter).");

        if (model.Position is < 1 or > 4)
            ModelState.AddModelError(nameof(model.Position), "Pozisyon 1-4 arasında olmalıdır.");

        if (model.RefreshIntervalSec is < 0 or > 86400)
            ModelState.AddModelError(nameof(model.RefreshIntervalSec), "Yenileme aralığı 0-86400 saniye olmalıdır.");

        if (model.ZoomFactor is < 0.25 or > 4)
            ModelState.AddModelError(nameof(model.ZoomFactor), "Zoom 0.25-4 arasında olmalıdır.");

        // Aynı ekran + pozisyonda başka aktif panel olmamalı (önceki Create kontrolü ekranı yok sayıyordu).
        if (model.IsActive && _context.SystemDashboardLinks.Any(x =>
                x.Id != (existingId ?? 0) && x.Screen == model.Screen && x.Position == model.Position && x.IsActive))
        {
            ModelState.AddModelError(string.Empty, "Bu ekran ve pozisyonda zaten aktif bir panel var.");
        }
    }

    private static object Snapshot(SystemDashboardLink x)
        => new { x.Title, x.Url, x.Screen, x.Position, x.RefreshIntervalSec, x.ZoomFactor, x.IsActive };
}

public static class DashboardPanelRules
{
    public static bool IsAllowedUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && !string.IsNullOrEmpty(uri.Host);

    public static bool IsValidScreenCode(string? screen)
        => !string.IsNullOrWhiteSpace(screen)
           && screen.Length <= 32
           && screen.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
}

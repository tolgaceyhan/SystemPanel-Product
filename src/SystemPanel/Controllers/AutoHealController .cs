using SystemPanel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Services;
using SystemPanel.ViewModels.AutoHeal;

[RequirePermission(Permissions.MonitoringManage)]
public class AutoHealController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IDpServiceStatusService _dpService;

    public AutoHealController(
        ApplicationDbContext db,
        IDpServiceStatusService dpService)
    {
        _db = db;
        _dpService = dpService;
    }

    // ✅ İL BAZLI AUTO-HEAL DURUMU
    public IActionResult Index()
    {
        var states = _db.AutoHealStates
            .OrderBy(x => x.ProvinceName)
            .ToList();

        return View(states);
    }

    public IActionResult Settings()
    {
        var s = _db.AutoHealSettings.FirstOrDefault()
            ?? new AutoHealSettings();

        return View(new AutoHealSettingsVm
        {
            CooldownMinutes = s.CooldownMinutes,
            Enabled = s.Enabled
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Settings(AutoHealSettingsVm vm)
    {
        var s = _db.AutoHealSettings.First();
        s.CooldownMinutes = vm.CooldownMinutes;
        s.Enabled = vm.Enabled;
        _db.SaveChanges();
        return RedirectToAction("Settings");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(string province)
    {
        var s = _db.AutoHealStates.FirstOrDefault(x => x.ProvinceName == province);
        if (s == null)
            return NotFound();

        s.Disabled = !s.Disabled;
        _db.SaveChanges();

        return Json(new { success = true, disabled = s.Disabled });
    }
}

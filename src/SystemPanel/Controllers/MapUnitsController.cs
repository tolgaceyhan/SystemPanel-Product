using Microsoft.AspNetCore.Mvc;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Services;
using SystemPanel.Extensions;
using SystemPanel.ViewModels;

[RequirePermission(Permissions.MonitoringView)]
public class MapUnitsController : Controller
{
    private readonly IMapUnitService _mapUnitService;
    private readonly IAdUserService _adUserService;
    private readonly IDpServiceStatusService _dpService;
    private readonly AutoHealService _autoHealService;

    public MapUnitsController(
        IMapUnitService mapUnitService,
        IAdUserService adUserService,
        IDpServiceStatusService dpService,
        AutoHealService autoHealService)
    {
        _mapUnitService = mapUnitService;
        _adUserService = adUserService;
        _dpService = dpService;
        _autoHealService = autoHealService;
    }

    public IActionResult Index() => View();

    // 🗺️ HARİTA API – TEK ve NİHAİ
    [HttpGet]
    public async Task<IActionResult> ProvinceStatus()
    {
        var provinces = _mapUnitService.GetAllProvinces();
        var result = new List<object>();

        foreach (var province in provinces)
        {
            var dn = _adUserService.BuildOuDn(province.ProvinceName);
            var dps = await _adUserService.GetDistributionServersByProvince(dn);

            int problem = 0;

            foreach (var dp in dps)
            {
                var s = await _dpService.GetStatus(dp.Name);
                if (!(s.wds && s.ccmexec))
                    problem++;
            }

            // DP verisi yoksa il "Healthy" DEĞİL "Unknown" gösterilir: veri yokluğu sağlık kanıtı değildir.
            // (GetDistributionServersByProvince şu an veri döndürmüyor; bkz. docs/CODEBASE_AUDIT.md §19.)
            string status =
                (dps.Count == 0) ? "Unknown" :
                (problem == 0) ? "Healthy" :
                (problem == dps.Count) ? "Down" :
                "Warning";

            // Auto-heal artık bir GET isteğinin yan etkisi olarak tetiklenmez; AutoHealWorker
            // (arka plan servisi) cooldown/disabled kurallarıyla yürütür.
            var healState = _autoHealService.GetState(province.ProvinceName);

            result.Add(new
            {
                province = province.ProvinceName.ToSvgId(),   // SVG için
                provinceName = province.ProvinceName,         // UI için
                status,
                totalDp = dps.Count,
                problemDp = problem,
                autoHealDisabled = healState?.Disabled ?? false,
                lastHealUtc = healState?.LastHealUtc
            });


        }

        return Json(result);
    }



// İl detay ekranı
public async Task<IActionResult> ProvinceDetails(string provinceName)
    {
        var dn = _adUserService.BuildOuDn(provinceName);
        var dps = await _adUserService.GetDistributionServersByProvince(dn);

        var list = new List<DpServerInfo>();

        foreach (var dp in dps)
        {
            var s = await _dpService.GetStatus(dp.Name);
            list.Add(new DpServerInfo
            {
                ServerName = dp.Name,
                IpAddress = dp.IpAddress,
                ProvinceName = provinceName,
                WdsRunning = s.wds,
                CcmExecRunning = s.ccmexec,
                AdditionalInfo = dp.Description
            });
        }

        return View(new ProvinceDetailsViewModel
        {
            ProvinceName = provinceName,
            DistributionServers = list
        });
    }
}

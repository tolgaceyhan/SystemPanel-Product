using System.ServiceProcess;
using SystemPanel.Services;

public class DpServiceStatusService : IDpServiceStatusService
{
    private readonly IAdUserService _adUserService;
    private readonly SccmLogService _sccm;

    public DpServiceStatusService(
        IAdUserService adUserService,
        SccmLogService sccm)
    {
        _adUserService = adUserService;
        _sccm = sccm;
    }

    public Task<(bool wds, bool ccmexec)> GetStatus(string server)
    {
        return Task.Run(() =>
        {
            return (
                IsRunning(server, "WDSServer"),
                IsRunning(server, "CcmExec")
            );
        });
    }

    private bool IsRunning(string machine, string service)
    {
        try
        {
            using var sc = new ServiceController(service, machine);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    // 🔥 AUTO-HEAL CORE
    public async Task<bool> RestartCriticalServicesByProvince(string provinceName)
    {
        var dn = _adUserService.BuildOuDn(provinceName);
        var dps = await _adUserService.GetDistributionServersByProvince(dn);

        bool anySuccess = false;

        foreach (var dp in dps)
        {
            try
            {
                Restart(dp.Name, "WDSServer");
                Restart(dp.Name, "CcmExec");
                anySuccess = true;
            }
            catch
            {
                // burada AutoHealLog yazılabilir
            }
        }

        return anySuccess;
    }

    private void Restart(string server, string service)
    {
        using var sc = new ServiceController(service, server);

        if (sc.Status == ServiceControllerStatus.Running)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }

        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }
}

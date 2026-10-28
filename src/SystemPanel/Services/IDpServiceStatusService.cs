namespace SystemPanel.Services
{
    public interface IDpServiceStatusService
    {
        Task<(bool wds, bool ccmexec)> GetStatus(string server);

        // 🔥 AUTO-HEAL – İL BAZLI RESTART
        Task<bool> RestartCriticalServicesByProvince(string provinceName);
    }
}


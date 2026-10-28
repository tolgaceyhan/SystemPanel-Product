using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class AutoHealService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AutoHealService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /* =====================================
           🔎 STATE OKUMA (HARİTA İÇİN)
        ===================================== */
        public AutoHealState? GetState(string province)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            return db.AutoHealStates
                .FirstOrDefault(x => x.ProvinceName == province);
        }

        /* =====================================
           🤖 AUTO-HEAL DENEME
           - Disabled kontrolü
           - Cooldown kontrolü
        ===================================== */
        public async Task<bool> TryHeal(string province)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dpService = scope.ServiceProvider.GetRequiredService<IDpServiceStatusService>();

            var settings = db.AutoHealSettings.FirstOrDefault();
            if (settings == null || !settings.Enabled)
                return false;

            var state = db.AutoHealStates
                .FirstOrDefault(x => x.ProvinceName == province);

            if (state == null)
            {
                state = new AutoHealState
                {
                    ProvinceName = province
                };
                db.AutoHealStates.Add(state);
            }

            // ⛔ İl bazlı kapalıysa
            if (state.Disabled)
                return false;

            // ⏱ Cooldown
            if (state.LastHealUtc.HasValue &&
                DateTime.UtcNow - state.LastHealUtc <
                TimeSpan.FromMinutes(settings.CooldownMinutes))
                return false;

            try
            {
                var ok = await dpService.RestartCriticalServicesByProvince(province);

                state.LastAttempt = DateTime.UtcNow;
                state.Success = ok;

                if (ok)
                    state.LastHealUtc = DateTime.UtcNow;

                db.AutoHealLogs.Add(new AutoHealLog
                {
                    Province = province,
                    Action = "AUTO_HEAL",
                    Success = ok
                });

                db.SaveChanges();
                return ok;
            }
            catch (Exception ex)
            {
                state.LastAttempt = DateTime.UtcNow;
                state.Success = false;
                state.ErrorMessage = ex.Message;

                db.AutoHealLogs.Add(new AutoHealLog
                {
                    Province = province,
                    Action = "AUTO_HEAL",
                    Success = false
                });

                db.SaveChanges();
                return false;
            }
        }
    }
}

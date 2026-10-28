using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class ServiceStatusService : IServiceStatusService
    {
        // Basit demo kaynak: bellekte tutulan liste
        // (Gerçekte SccmLogService’den doldurabilirsiniz)
        private readonly List<ServiceStatus> _items = new()
        {
            new ServiceStatus { Id = 1, ServiceName = "CcmExec",       State = "Running",  RawStatus = "OK" },
            new ServiceStatus { Id = 2, ServiceName = "SMS_EXECUTIVE", State = "Stopped",  RawStatus = "Stopped" },
            new ServiceStatus { Id = 3, ServiceName = "WDS",           State = "Running",  RawStatus = "OK" },
            new ServiceStatus { Id = 4, ServiceName = "WDSServer",     State = "Stopped",  RawStatus = "Stopped" },
        };

        public Task<IEnumerable<ServiceStatus>> GetAllServiceStatusesAsync()
            => Task.FromResult<IEnumerable<ServiceStatus>>(_items);

        public ServiceStatus GetServiceStatusById(int id)
            => _items.FirstOrDefault(x => x.Id == id);

        public Task StartServiceAsync(int id)
        {
            var svc = _items.FirstOrDefault(x => x.Id == id);
            if (svc != null)
            {
                svc.State = "Running";
                svc.RawStatus = "OK";
                // svc.Note = null; // isterseniz kritik notu temizleyin
            }
            return Task.CompletedTask;
        }

        public Task StopServiceAsync(int id)
        {
            var svc = _items.FirstOrDefault(x => x.Id == id);
            if (svc != null)
            {
                svc.State = "Stopped";
                svc.RawStatus = "Stopped";
            }
            return Task.CompletedTask;
        }
    }
}

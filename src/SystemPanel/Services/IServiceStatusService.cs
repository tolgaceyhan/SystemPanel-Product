using System.Collections.Generic;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public interface IServiceStatusService
    {
        Task<IEnumerable<ServiceStatus>> GetAllServiceStatusesAsync();
        Task StartServiceAsync(int id);
        Task StopServiceAsync(int id);
        ServiceStatus GetServiceStatusById(int id);  // Yeni eklenen metod
    }

}


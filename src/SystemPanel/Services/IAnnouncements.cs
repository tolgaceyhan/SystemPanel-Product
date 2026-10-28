using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SystemPanel.Models;
using SystemPanel.ViewModels.Announcements;

namespace SystemPanel.Services
{
    public interface IAnnouncementsService
    {
        // Query için
        IQueryable<Announcements> Query();

        Task<int> CountAsync();
        Task<int> TodayCountAsync();
        Task<Announcements?> GetLastAsync();

        Task<IEnumerable<Announcements>> GetAllAsync();
        Task<Announcements?> GetByIdAsync(int id);

        Task AddAsync(CreateAnnouncementViewModel model);
        Task UpdateAsync(EditAnnouncementViewModel model);
        Task DeleteAsync(int id);
        Task DeleteRangeAsync(int[] ids);
    }
}

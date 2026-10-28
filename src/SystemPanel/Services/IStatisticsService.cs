using System.Threading.Tasks;
using SystemPanel.ViewModels.Statistics;

namespace SystemPanel.Services;

public interface IStatisticsService
{
    Task<StatisticsViewModel> GetStatisticsAsync();
}

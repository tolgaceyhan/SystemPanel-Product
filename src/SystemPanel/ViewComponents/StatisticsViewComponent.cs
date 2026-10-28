using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using SystemPanel.Services;

namespace SystemPanel.ViewComponents
{
    public class StatisticsViewComponent : ViewComponent
    {
        private readonly IStatisticsService _service;

        public StatisticsViewComponent(IStatisticsService service)
        {
            _service = service;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var model = await _service.GetStatisticsAsync();
            return View(model);
        }
    }
}

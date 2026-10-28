using Microsoft.AspNetCore.Mvc;
using SystemPanel.Security;
using SystemPanel.Models;
using SystemPanel.Services;
using SystemPanel.ViewModels;

namespace SystemPanel.Controllers
{
    // NOT: IServiceStatusService bir MOCK'tur (bellek içi demo liste); gerçek servis durumu göstermez.
    [RequirePermission(Permissions.ServersView)]
    public class ServicesController : Controller
    {
        private readonly IServiceStatusService _service;

        public ServicesController(IServiceStatusService service)
        {
            _service = service;
        }

        // Servislerin durumunu görüntüleme
        public async Task<IActionResult> Status()
        {
            var services = await _service.GetAllServiceStatusesAsync();
            var viewModel = new ServiceStatusViewModel
            {
                Services = services.ToList()
            };
            return View(viewModel);  // View'a veriyi gönderiyoruz
        }

        // Servisi başlatma
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.ServersServicesControl)]
        public async Task<IActionResult> Start(int id)
        {
            await _service.StartServiceAsync(id);
            return RedirectToAction(nameof(Status));
        }

        // Servisi durdurma
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.ServersServicesControl)]
        public async Task<IActionResult> Stop(int id)
        {
            await _service.StopServiceAsync(id);
            return RedirectToAction(nameof(Status));
        }
    }
}

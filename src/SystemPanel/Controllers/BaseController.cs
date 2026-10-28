using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

public class BaseController : Controller
{
    protected readonly ILogger _logger;

    public BaseController(ILogger logger)
    {
        _logger = logger;
    }

    // Genel hata yönetimi için bir yöntem
    protected async Task<IActionResult> ExecuteWithLogging(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action(); // Asenkron işlem yapılır
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bir hata oluştu."); // Corrected logger reference
            return View("Error"); // Hata sayfasına yönlendirilir.
        }
    }
}
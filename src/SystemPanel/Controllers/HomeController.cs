using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemPanel.Infrastructure;
using SystemPanel.ViewModels;

[Authorize]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        ViewData["Role"] =
            User.IsInRole("Admin") ? "Admin" :
            User.IsInRole("LocalAdmin") ? "LocalAdmin" :
            "User";

        return View();
    }

    /// <summary>
    /// Genel hata sayfası: kullanıcıya teknik ayrıntı GÖSTERMEZ, yalnızca destek için
    /// correlation ID verir. Ayrıntılar uygulama logunda aynı ID ile bulunur.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        Response.StatusCode = statusCode is >= 400 and < 600 ? statusCode.Value : StatusCodes.Status500InternalServerError;

        // API isteğinde oluşan hata: HTML değil ProblemDetails (correlationId eklentisiyle).
        var failedPath = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>()?.Path;
        if (failedPath?.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
            return Problem(title: "Beklenmeyen bir hata oluştu.", statusCode: Response.StatusCode);

        return View("Error", new ErrorViewModel
        {
            RequestId = CorrelationIdMiddleware.Get(HttpContext),
            Message = statusCode == 404 ? "Aradığınız sayfa bulunamadı." : "İşlem sırasında beklenmeyen bir hata oluştu.",
            Detail = string.Empty,
        });
    }
}


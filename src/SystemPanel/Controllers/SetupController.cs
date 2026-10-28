using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SystemPanel.Auditing;
using SystemPanel.Setup;

namespace SystemPanel.Controllers
{
    /// <summary>
    /// İlk kurulum sihirbazı. [AllowAnonymous] gerekçesi: henüz hiçbir kullanıcı yokken ilk yöneticiyi
    /// oluşturmak. Korumalar: yalnızca kurulum gerekiyorsa çalışır (aksi halde 404), sunucudaki tek
    /// kullanımlık belirteci ister, IP başına hız sınırı uygular ve sonucu audit'ler.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(SetupRateLimit.PolicyName)]
    [Route("setup")]
    public class SetupController : Controller
    {
        private readonly SetupService _setup;
        private readonly IAuditService _audit;

        public SetupController(SetupService setup, IAuditService audit)
        {
            _setup = setup;
            _audit = audit;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            if (!await _setup.IsSetupRequiredAsync(ct))
                return NotFound();

            await _setup.EnsureTokenAsync(ct);
            ViewBag.TokenFilePath = _setup.TokenFilePath;
            return View(new SetupViewModel());
        }

        [HttpPost("")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SetupViewModel model, CancellationToken ct)
        {
            if (!await _setup.IsSetupRequiredAsync(ct))
                return NotFound();

            ViewBag.TokenFilePath = _setup.TokenFilePath;
            if (!ModelState.IsValid)
                return View(model);

            var result = await _setup.CompleteAsync(model.Token, model.UserName, model.Password, ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.SetupCompleted)
            {
                ResourceType = "IdentityUser",
                ResourceName = model.UserName,
                Success = result.Success,
                ErrorMessage = result.Error,
                ActorUserName = "(kurulum)",
            }, ct);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Kurulum tamamlanamadı.");
                return View(model);
            }

            TempData["Success"] = "İlk yönetici oluşturuldu. Giriş yapabilirsiniz.";
            return RedirectToAction("Login", "Account");
        }
    }

    public sealed class SetupViewModel
    {
        [Required(ErrorMessage = "Kurulum belirteci gerekli.")]
        [Display(Name = "Kurulum belirteci")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
        [StringLength(64, MinimumLength = 3)]
        [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Yalnızca harf, rakam, nokta, tire ve alt çizgi.")]
        [Display(Name = "Yönetici kullanıcı adı")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Parola gerekli.")]
        [StringLength(256, MinimumLength = 12, ErrorMessage = "Parola en az 12 karakter olmalı.")]
        [DataType(DataType.Password)]
        [Display(Name = "Parola")]
        public string Password { get; set; } = string.Empty;

        [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
        [DataType(DataType.Password)]
        [Display(Name = "Parola (tekrar)")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public static class SetupRateLimit
    {
        public const string PolicyName = "setup";
    }
}

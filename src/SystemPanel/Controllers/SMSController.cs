using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SystemPanel.Auditing;
using SystemPanel.Security;
using SystemPanel.Security.Scope;
using SystemPanel.Services;
using SystemPanel.ViewModels;

/// <summary>
/// SMS gönderimi. Önceden tamamen anonimdi (tüm AD kullanıcılarına toplu SMS dahil).
/// Tekil gönderim sms.send, grup/OU/toplu gönderimler sms.bulk izni gerektirir; her gönderim audit'lenir.
/// Mesaj içeriği audit'e yazılmaz (yalnızca uzunluk); içerik SmsLogService ile Smsler tablosunda tutulur.
/// </summary>
[RequirePermission(Permissions.SmsSend)]
public class SMSController : Controller
{
    private const int MaxMessageLength = 1000;

    private readonly ISmsService _sms;
    private readonly IAdDirectoryService _ad;
    private readonly ISmsLogService _smsLog;
    private readonly IAuditService _audit;
    private readonly ILogger<SMSController> _logger;

    public SMSController(
        ISmsService sms,
        IAdDirectoryService ad,
        ISmsLogService smsLog,
        IAuditService audit,
        ILogger<SMSController> logger)
    {
        _sms = sms;
        _ad = ad;
        _smsLog = smsLog;
        _audit = audit;
        _logger = logger;
    }

    // ---------------------------------------------------------
    // 1) Tekil SMS Gönderimi
    // ---------------------------------------------------------
    [HttpGet]
    public IActionResult Send() => View(new SMSRequest());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(SMSRequest vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var recipients = new[] { new AdUserDto { DisplayName = vm.Phone, Phone = vm.Phone } };
        var (sent, failed) = await SendAllAsync(recipients, vm.Message, "Tekil");

        TempData["ok"] = failed == 0 ? "SMS gönderildi." : "SMS gönderilemedi.";
        return RedirectToAction(nameof(Send));
    }

    // ---------------------------------------------------------
    // 2) Local Adminlere SMS
    // ---------------------------------------------------------
    [HttpPost, ValidateAntiForgeryToken]
    [RequirePermission(Permissions.SmsBulkSend)]
    public async Task<IActionResult> SendToLocalAdmins(string message)
        => await BulkAsync(await _ad.GetLocalAdminsAsync(), message, "Local Adminler");

    // ---------------------------------------------------------
    // 3) Şube Müdürlerine SMS
    // ---------------------------------------------------------
    [HttpPost, ValidateAntiForgeryToken]
    [RequirePermission(Permissions.SmsBulkSend)]
    public async Task<IActionResult> SendToSubeMudurlari(string message)
        => await BulkAsync(await _ad.GetSubeMudurlariAsync(), message, "Şube Müdürleri");

    // ---------------------------------------------------------
    // 4) Tüm AD Kullanıcılarına SMS
    // ---------------------------------------------------------
    [HttpPost, ValidateAntiForgeryToken]
    [RequirePermission(Permissions.SmsBulkSend)]
    public async Task<IActionResult> SendToAllAdUsers(string message)
        => await BulkAsync(await _ad.GetAllUsersAsync(), message, "Tüm AD Kullanıcıları");

    // ---------------------------------------------------------
    // 5) OU bazlı SMS gönderimi
    // ---------------------------------------------------------
    [HttpPost, ValidateAntiForgeryToken]
    [RequirePermission(Permissions.SmsBulkSend)]
    public async Task<IActionResult> SendToOu(string ouDn, string message)
    {
        if (!DistinguishedName.TryParse(ouDn, out var dn))
        {
            TempData["ok"] = "Geçersiz OU.";
            return RedirectToAction(nameof(Send));
        }

        return await BulkAsync(await _ad.GetUsersByOuAsync(dn.ToString()), message, $"OU: {dn}");
    }

    // ---------------------------------------------------------
    // 6) Toplu Seçim (AJAX)
    // ---------------------------------------------------------
    [HttpPost, ValidateAntiForgeryToken]
    [RequirePermission(Permissions.SmsBulkSend)]
    public async Task<IActionResult> SendToSelection([FromBody] SmsSelectionRequest req)
    {
        if (req?.DistinguishedNames == null || string.IsNullOrWhiteSpace(req.Message) || req.Message.Length > MaxMessageLength)
            return BadRequest(new { error = "Geçersiz istek." });

        var dns = req.DistinguishedNames
            .Where(d => DistinguishedName.TryParse(d, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var users = await _ad.GetUsersByDnsAsync(dns);
        var (sent, failed) = await SendAllAsync(users, req.Message, "Toplu Seçim");

        return Ok(new { Count = sent, Failed = failed });
    }

    private async Task<IActionResult> BulkAsync(List<AdUserDto> users, string message, string group)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
        {
            TempData["ok"] = "Mesaj boş veya çok uzun.";
            return RedirectToAction(nameof(Send));
        }

        var (sent, failed) = await SendAllAsync(users, message, group);
        TempData["ok"] = failed == 0
            ? $"{group}: SMS gönderildi ({sent} kişi)."
            : $"{group}: {sent} kişiye gönderildi, {failed} gönderim başarısız.";
        return RedirectToAction(nameof(Send));
    }

    private async Task<(int Sent, int Failed)> SendAllAsync(IReadOnlyCollection<AdUserDto> users, string message, string group)
    {
        int sent = 0, failed = 0;
        foreach (var u in users)
        {
            try
            {
                await _sms.SendAsync(u.Phone, message);
                sent++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "SMS gönderilemedi ({Group}).", group);
            }
        }

        await _smsLog.LogAsync(message, group, users);

        await _audit.RecordAsync(new AuditRecord(AuditActions.SmsSend)
        {
            ResourceType = "Sms",
            ResourceName = group,
            Success = failed == 0,
            Metadata = new { Group = group, Recipients = users.Count, Sent = sent, Failed = failed, MessageLength = message?.Length ?? 0 },
        });

        return (sent, failed);
    }
}

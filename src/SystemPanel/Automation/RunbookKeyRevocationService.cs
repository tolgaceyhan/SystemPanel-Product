using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SystemPanel.Automation
{
    public sealed record KeyRevocationResult(bool Success, string? Error, int CancelledRuns = 0, int DisabledSchedules = 0);

    /// <summary>
    /// Kurum imzalama anahtarı iptali (ADR-002 Faz 4). Anahtar iptal edilir; o anahtarla imzalı sürümlerin bekleyen
    /// talepleri iptal edilir ve zamanlamaları kapatılır. İptal edilmiş anahtarla imzalı paketler zaten
    /// <see cref="RunbookCatalog.VerifyAsync"/> tarafından reddedilir; bu adım kuyrukta "ölü" iş bırakmamak içindir.
    /// Üretici (yerleşik) anahtarı panelden iptal edilemez; ürün güncellemesiyle değişir.
    /// </summary>
    public sealed class RunbookKeyRevocationService
    {
        private readonly RunbookTrustStore _trust;
        private readonly RunbookRunService _runs;
        private readonly RunbookScheduleService _schedules;

        public RunbookKeyRevocationService(RunbookTrustStore trust, RunbookRunService runs, RunbookScheduleService schedules)
        {
            _trust = trust;
            _runs = runs;
            _schedules = schedules;
        }

        public async Task<KeyRevocationResult> RevokeAsync(Guid keyId, string? actor, CancellationToken ct = default)
        {
            var key = (await _trust.ListAsync(ct)).FirstOrDefault(k => !k.IsBuiltIn && k.Id == keyId);
            if (key == null)
                return new KeyRevocationResult(false, "Anahtar bulunamadı.");
            if (key.IsRevoked)
                return new KeyRevocationResult(false, "Anahtar zaten iptal edilmiş.");
            if (!await _trust.RevokeAsync(keyId, actor, ct))
                return new KeyRevocationResult(false, "Anahtar iptal edilemedi.");

            var who = string.IsNullOrWhiteSpace(actor) ? "automation" : actor;
            var runs = await _runs.CancelSignedByAsync(key.Fingerprint, who, ct);
            var schedules = await _schedules.DisableSignedByAsync(key.Fingerprint, who, ct);
            return new KeyRevocationResult(true, null, runs, schedules);
        }
    }
}

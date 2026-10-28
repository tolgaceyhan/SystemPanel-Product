using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Auditing;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Automation
{
    public sealed record TrustedRunbookKey(string Name, string Fingerprint, string PublicKeyPem, bool IsBuiltIn, Guid? Id, bool IsRevoked);

    /// <summary>Üretici anahtarının kaynağı (derlemeye gömülü). Testlerde değiştirilebilir.</summary>
    public interface IVendorRunbookKeyProvider
    {
        string? GetPublicKeyPem();
    }

    public sealed class EmbeddedVendorRunbookKeyProvider : IVendorRunbookKeyProvider
    {
        public const string ResourceName = "SystemPanel.Automation.runbook-vendor-public.pem";

        public string? GetPublicKeyPem()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Runbook imzalama anahtarları (ADR-002 §2): gömülü üretici anahtarı + kurumun kaydettiği anahtarlar.
    /// İptal edilmiş anahtarla imzalanmış paketler içe aktarılamaz ve çalıştırılamaz.
    /// </summary>
    public sealed class RunbookTrustStore
    {
        private readonly ApplicationDbContext _db;
        private readonly IVendorRunbookKeyProvider _vendor;
        private readonly IAuditService _audit;

        public RunbookTrustStore(ApplicationDbContext db, IVendorRunbookKeyProvider vendor, IAuditService audit)
        {
            _db = db;
            _vendor = vendor;
            _audit = audit;
        }

        public async Task<IReadOnlyList<TrustedRunbookKey>> ListAsync(CancellationToken ct = default)
        {
            var result = new List<TrustedRunbookKey>();
            var vendorPem = _vendor.GetPublicKeyPem();
            if (!string.IsNullOrWhiteSpace(vendorPem))
            {
                using var key = RunbookPackageCodec.ImportPublicKeyPem(vendorPem);
                result.Add(new TrustedRunbookKey("Üretici (yerleşik)", RunbookPackageCodec.Fingerprint(key), vendorPem, true, null, false));
            }

            result.AddRange((await _db.RunbookSigningKeys.AsNoTracking().OrderBy(k => k.Name).ToListAsync(ct))
                .Select(k => new TrustedRunbookKey(k.Name, k.Fingerprint, k.PublicKeyPem, false, k.Id, k.IsRevoked)));
            return result;
        }

        /// <summary>Parmak izine karşılık gelen, iptal edilmemiş anahtar.</summary>
        public async Task<TrustedRunbookKey?> FindActiveAsync(string fingerprint, CancellationToken ct = default)
            => (await ListAsync(ct)).FirstOrDefault(k => !k.IsRevoked && string.Equals(k.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

        public async Task<(bool Ok, string? Error)> AddAsync(string name, string publicKeyPem, string? addedBy, CancellationToken ct = default)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length is < 1 or > 100)
                return (false, "Ad 1-100 karakter olmalı.");

            // Özel anahtar yapıştırıldıysa kabul edilmez (ImportFromPem açık anahtarı türetebilirdi): anahtar artık ifşa olmuştur.
            if ((publicKeyPem ?? string.Empty).Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                return (false, "Bu bir ÖZEL anahtar. Kaydedilmedi. Özel anahtar paylaşıldığı için ifşa olmuş sayılmalıdır: yeni bir anahtar çifti üretin ve yalnızca açık anahtarı (runbook-org-public.pem) kaydedin.");

            string fingerprint;
            string normalizedPem;
            try
            {
                using var key = ECDsa.Create();
                key.ImportFromPem(publicKeyPem);
                if (key.KeySize != 256)
                    return (false, "Yalnızca ECDSA P-256 açık anahtarları desteklenir.");
                fingerprint = RunbookPackageCodec.Fingerprint(key);
                normalizedPem = key.ExportSubjectPublicKeyInfoPem();
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                return (false, "Geçerli bir PEM açık anahtarı değil.");
            }

            if ((await ListAsync(ct)).Any(k => k.Fingerprint == fingerprint))
                return (false, "Bu anahtar zaten kayıtlı.");

            var entity = new RunbookSigningKey
            {
                Id = Guid.NewGuid(),
                Name = name,
                Fingerprint = fingerprint,
                PublicKeyPem = normalizedPem,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = addedBy,
            };
            _db.RunbookSigningKeys.Add(entity);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationKeyAdd)
            {
                ResourceType = "RunbookSigningKey",
                ResourceId = fingerprint,
                ResourceName = name,
            }, ct);

            return (true, null);
        }

        public async Task<bool> RevokeAsync(Guid id, string? revokedBy, CancellationToken ct = default)
        {
            var key = await _db.RunbookSigningKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
            if (key == null || key.IsRevoked)
                return false;

            key.IsRevoked = true;
            key.RevokedAtUtc = DateTime.UtcNow;
            key.RevokedBy = revokedBy;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationKeyRevoke)
            {
                ResourceType = "RunbookSigningKey",
                ResourceId = key.Fingerprint,
                ResourceName = key.Name,
            }, ct);

            return true;
        }
    }
}

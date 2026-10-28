using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SystemPanel.Auditing;
using SystemPanel.Contracts.Automation;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Automation
{
    public sealed record RunbookImportResult(bool Success, string? Error, RunbookVersion? Version, bool AlreadyPresent = false);

    /// <summary>Runbook kataloğu: imzalı paketlerin içe aktarılması, listelenmesi ve yeniden doğrulanması.</summary>
    public sealed class RunbookCatalog
    {
        private readonly ApplicationDbContext _db;
        private readonly RunbookTrustStore _trust;
        private readonly IAuditService _audit;
        private readonly ILogger<RunbookCatalog> _logger;

        public RunbookCatalog(ApplicationDbContext db, RunbookTrustStore trust, IAuditService audit, ILogger<RunbookCatalog> logger)
        {
            _db = db;
            _trust = trust;
            _audit = audit;
            _logger = logger;
        }

        public async Task<RunbookImportResult> ImportAsync(string packageText, string? importedBy, CancellationToken ct = default)
        {
            var result = await ImportCoreAsync(packageText, importedBy, ct);

            if (!result.AlreadyPresent)
            {
                await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunbookImport)
                {
                    ResourceType = "Runbook",
                    ResourceId = result.Version?.RunbookId,
                    ResourceName = result.Version == null ? null : $"{result.Version.RunbookId}@{result.Version.Version}",
                    Success = result.Success,
                    ErrorMessage = result.Error,
                    After = result.Version == null ? null : new { result.Version.Risk, result.Version.KeyFingerprint, result.Version.ScriptSha256 },
                }, ct);
            }

            return result;
        }

        private async Task<RunbookImportResult> ImportCoreAsync(string packageText, string? importedBy, CancellationToken ct)
        {
            if (!RunbookPackageCodec.TryReadKeyId(packageText, out var keyId, out var error))
                return new RunbookImportResult(false, error, null);

            var key = await _trust.FindActiveAsync(keyId!, ct);
            if (key == null)
                return new RunbookImportResult(false, "Paketi imzalayan anahtar güvenilen anahtarlar arasında değil veya iptal edilmiş.", null);

            using var publicKey = RunbookPackageCodec.ImportPublicKeyPem(key.PublicKeyPem);
            if (!RunbookPackageCodec.TryVerify(packageText, publicKey, out var package, out error))
                return new RunbookImportResult(false, error, null);

            var m = package!.Manifest;
            var existing = await _db.RunbookVersions.FirstOrDefaultAsync(v => v.RunbookId == m.Id && v.Version == m.Version, ct);
            if (existing != null)
            {
                return existing.ScriptSha256 == m.ScriptSha256 && existing.KeyFingerprint == package.KeyFingerprint
                    ? new RunbookImportResult(true, null, existing, AlreadyPresent: true)
                    : new RunbookImportResult(false, $"{m.Id}@{m.Version} farklı içerikle zaten kayıtlı. Sürüm numarasını artırın.", null);
            }

            var ownershipError = await CheckOwnershipAsync(m.Id, key, ct);
            if (ownershipError != null)
                return new RunbookImportResult(false, ownershipError, null);

            var version = new RunbookVersion
            {
                Id = Guid.NewGuid(),
                RunbookId = m.Id,
                Version = m.Version,
                Title = m.Title,
                Description = m.Description,
                Risk = m.Risk,
                TargetType = m.TargetType,
                Executor = m.Executor,
                SupportsDryRun = m.SupportsDryRun,
                ScriptSha256 = m.ScriptSha256,
                KeyFingerprint = package.KeyFingerprint,
                PackageText = packageText,
                IsEnabled = true,
                ImportedAtUtc = DateTime.UtcNow,
                ImportedBy = importedBy,
            };
            _db.RunbookVersions.Add(version);
            await _db.SaveChangesAsync(ct);

            return new RunbookImportResult(true, null, version);
        }

        /// <summary>
        /// Runbook kimliğinin sahipliği (Faz 4): <c>org.</c> ön eki kurum anahtarlarına ayrılmıştır; bir kimlik ilk kez hangi
        /// kaynağın (üretici/kurum) anahtarıyla içe aktarıldıysa sonraki sürümleri de aynı kaynaktan gelmelidir. Böylece kurum
        /// anahtarı yerleşik bir runbook'un "yeni sürümünü" yayımlayıp onun yerine geçemez; ürün güncellemesi de kurumun
        /// runbook'unu ezemez. Kurum içinde anahtar değişimi (rotasyon) serbesttir.
        /// </summary>
        private async Task<string?> CheckOwnershipAsync(string runbookId, TrustedRunbookKey key, CancellationToken ct)
        {
            var isOrganizationKey = !key.IsBuiltIn;
            if (!isOrganizationKey && RunbookNamespaces.IsOrganizationId(runbookId))
                return $"'{RunbookNamespaces.OrganizationPrefix}' ön eki kurum runbook'larına ayrılmıştır; üretici paketi bu kimliği kullanamaz.";

            var organizationKeys = await _db.RunbookSigningKeys.AsNoTracking().Select(k => k.Fingerprint).ToListAsync(ct);
            var signers = await _db.RunbookVersions.AsNoTracking().Where(v => v.RunbookId == runbookId).Select(v => v.KeyFingerprint).Distinct().ToListAsync(ct);
            if (signers.Any(fp => organizationKeys.Contains(fp, StringComparer.OrdinalIgnoreCase) != isOrganizationKey))
            {
                return isOrganizationKey
                    ? $"{runbookId} üretici kataloğuna ait; kurum anahtarıyla yeni sürümü yayımlanamaz. Kurum runbook'ları '{RunbookNamespaces.OrganizationPrefix}' ön ekini kullanır."
                    : $"{runbookId} kurum anahtarıyla yayımlanmış; üretici paketi bu kimliği kullanamaz.";
            }

            return null;
        }

        /// <summary>Klasördeki *.sprb paketlerini içe aktarır (açılışta; yerleşik katalog).</summary>
        public async Task<int> ImportDirectoryAsync(string directory, CancellationToken ct = default)
        {
            if (!Directory.Exists(directory))
                return 0;

            var imported = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*.sprb").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                if (info.Length > RunbookPackageCodec.MaxPackageBytes)
                {
                    _logger.LogWarning("Runbook paketi çok büyük, atlandı: {File}", info.Name);
                    continue;
                }

                var result = await ImportAsync(await File.ReadAllTextAsync(file, ct), "(katalog)", ct);
                if (!result.Success)
                    _logger.LogWarning("Runbook paketi içe aktarılamadı: {File}: {Error}", info.Name, result.Error);
                else if (!result.AlreadyPresent)
                    imported++;
            }

            return imported;
        }

        /// <summary>Her runbook'un etkin en son sürümü.</summary>
        public async Task<IReadOnlyList<RunbookVersion>> ListLatestAsync(CancellationToken ct = default)
        {
            var all = await _db.RunbookVersions.AsNoTracking().Where(v => v.IsEnabled).ToListAsync(ct);
            return all
                .GroupBy(v => v.RunbookId)
                .Select(g => g.OrderByDescending(v => ParseVersion(v.Version)).First())
                .OrderBy(v => v.Title)
                .ToList();
        }

        public Task<List<RunbookVersion>> ListAllAsync(CancellationToken ct = default)
            => _db.RunbookVersions.AsNoTracking().OrderBy(v => v.RunbookId).ThenByDescending(v => v.ImportedAtUtc).ToListAsync(ct);

        public async Task<bool> SetEnabledAsync(Guid versionId, bool enabled, CancellationToken ct = default)
        {
            var version = await _db.RunbookVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version == null)
                return false;

            version.IsEnabled = enabled;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AutomationRunbookDisable)
            {
                ResourceType = "Runbook",
                ResourceId = version.RunbookId,
                ResourceName = $"{version.RunbookId}@{version.Version}",
                After = new { Enabled = enabled },
            }, ct);
            return true;
        }

        public Task<RunbookVersion?> FindAsync(Guid versionId, CancellationToken ct = default)
            => _db.RunbookVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);

        /// <summary>
        /// Saklanan paketi yeniden doğrular (anahtar hâlâ güvenilir mi, içerik değişmemiş mi). Önizleme ve
        /// çalıştırma öncesinde çağrılır (TOCTOU koruması).
        /// </summary>
        public async Task<(VerifiedRunbookPackage? Package, string? Error)> VerifyAsync(RunbookVersion version, CancellationToken ct = default)
        {
            if (!version.IsEnabled)
                return (null, "Runbook sürümü devre dışı.");

            var key = await _trust.FindActiveAsync(version.KeyFingerprint, ct);
            if (key == null)
                return (null, "Runbook'u imzalayan anahtar artık güvenilir değil (iptal edilmiş veya silinmiş).");

            using var publicKey = RunbookPackageCodec.ImportPublicKeyPem(key.PublicKeyPem);
            if (!RunbookPackageCodec.TryVerify(version.PackageText, publicKey, out var package, out var error))
                return (null, error);

            if (package!.Manifest.ScriptSha256 != version.ScriptSha256 || package.Manifest.Id != version.RunbookId || package.Manifest.Version != version.Version)
                return (null, "Saklanan runbook kaydı paketle uyuşmuyor.");

            return (package, null);
        }

        internal static Version ParseVersion(string value)
            => System.Version.TryParse(value, out var v) ? v : new Version(0, 0);
    }
}

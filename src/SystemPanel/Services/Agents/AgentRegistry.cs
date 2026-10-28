using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Auditing;
using SystemPanel.Data;
using SystemPanel.Licensing;
using SystemPanel.Models;

namespace SystemPanel.Services.Agents
{
    public sealed record AgentCredentials(Guid AgentId, string Hostname, string Key);

    /// <summary>Agent kaydı, anahtar yenileme ve etkinleştirme. Anahtar yalnızca dönüş değerinde bir kez bulunur.</summary>
    public sealed class AgentRegistry
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _audit;
        private readonly ILicenseService _license;

        public AgentRegistry(ApplicationDbContext db, IAuditService audit, ILicenseService license)
        {
            _db = db;
            _audit = audit;
            _license = license;
        }

        public Task<List<Agent>> ListAsync(CancellationToken ct = default)
            => _db.Agents.AsNoTracking().OrderBy(a => a.Hostname).ToListAsync(ct);

        public async Task<AgentCredentials> RegisterAsync(string hostname, string? province, string? description, string? createdBy, CancellationToken ct = default)
        {
            var host = MetricsIngestionService.ValidateHostname(hostname);

            if (await _db.Agents.AnyAsync(a => a.Hostname == host, ct))
                throw new InvalidOperationException($"{host} için zaten kayıtlı bir agent var. Anahtarı yenilemeyi kullanın.");

            await EnsureWithinLicenseAsync(ct);

            var key = AgentKeys.Generate();
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Hostname = host,
                Province = Trim(province, 100),
                Description = Trim(description, 256),
                KeyHash = AgentKeys.Hash(key),
                KeyPrefix = AgentKeys.DisplayPrefix(key),
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = createdBy,
            };

            _db.Agents.Add(agent);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AgentRegister)
            {
                ResourceType = "Agent",
                ResourceId = agent.Id.ToString(),
                ResourceName = host,
                After = new { agent.Hostname, agent.Province, agent.KeyPrefix },
            }, ct);

            return new AgentCredentials(agent.Id, host, key);
        }

        public async Task<AgentCredentials?> RotateKeyAsync(Guid id, CancellationToken ct = default)
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent == null) return null;

            var key = AgentKeys.Generate();
            var oldPrefix = agent.KeyPrefix;
            agent.KeyHash = AgentKeys.Hash(key);
            agent.KeyPrefix = AgentKeys.DisplayPrefix(key);
            agent.KeyRotatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(AuditActions.AgentKeyRotate)
            {
                ResourceType = "Agent",
                ResourceId = agent.Id.ToString(),
                ResourceName = agent.Hostname,
                Before = new { KeyPrefix = oldPrefix },
                After = new { agent.KeyPrefix },
            }, ct);

            return new AgentCredentials(agent.Id, agent.Hostname, key);
        }

        public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent == null) return false;

            if (enabled && !agent.IsEnabled)
                await EnsureWithinLicenseAsync(ct);

            agent.IsEnabled = enabled;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord(enabled ? AuditActions.AgentEnable : AuditActions.AgentDisable)
            {
                ResourceType = "Agent",
                ResourceId = agent.Id.ToString(),
                ResourceName = agent.Hostname,
            }, ct);

            return true;
        }

        public async Task<bool> UpdateProvinceAsync(Guid id, string? province, CancellationToken ct = default)
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent == null) return false;

            var before = agent.Province;
            agent.Province = Trim(province, 100);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(new AuditRecord("agent.update")
            {
                ResourceType = "Agent",
                ResourceId = agent.Id.ToString(),
                ResourceName = agent.Hostname,
                Before = new { Province = before },
                After = new { agent.Province },
            }, ct);
            return true;
        }

        /// <summary>Etkin agent sayısı lisans sınırını aşamaz. (Mevcut agent'lar sınır düşse bile çalışmaya devam eder.)</summary>
        private async Task EnsureWithinLicenseAsync(CancellationToken ct)
        {
            var max = _license.MaxAgents;
            if (max == null)
                return;

            var enabledCount = await _db.Agents.CountAsync(a => a.IsEnabled, ct);
            if (enabledCount >= max.Value)
                throw new InvalidOperationException($"Lisans sınırına ulaşıldı: en fazla {max.Value} etkin agent. Lisansınızı yükseltin veya kullanılmayan bir agent'ı devre dışı bırakın.");
        }

        private static string? Trim(string? value, int max)
        {
            var v = value?.Trim();
            return string.IsNullOrEmpty(v) ? null : v.Length > max ? v.Substring(0, max) : v;
        }
    }
}

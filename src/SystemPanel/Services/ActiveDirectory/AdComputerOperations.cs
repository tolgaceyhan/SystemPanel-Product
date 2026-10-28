using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Auditing;
using SystemPanel.Models;
using SystemPanel.Security.Scope;

namespace SystemPanel.Services.ActiveDirectory
{
    public interface IAdComputerOperations
    {
        Task<List<AdComputer>> SearchAsync(ClaimsPrincipal actor, string? term, CancellationToken ct = default);
        Task<AdComputer?> GetAsync(ClaimsPrincipal actor, string name, CancellationToken ct = default);

        /// <summary>LAPS parolasını kapsam kontrolü ve audit ile okur. Çağıran laps.read iznini zorunlu kılmalıdır.</summary>
        Task<(AdOperationResult Result, LapsResult? Laps)> RevealLapsAsync(ClaimsPrincipal actor, string name, CancellationToken ct = default);
    }

    public sealed class AdComputerOperations : IAdComputerOperations
    {
        private readonly IAdComputerService _ad;
        private readonly IOrganizationalScopeResolver _scopes;
        private readonly IAuditService _audit;

        public AdComputerOperations(IAdComputerService ad, IOrganizationalScopeResolver scopes, IAuditService audit)
        {
            _ad = ad;
            _scopes = scopes;
            _audit = audit;
        }

        public async Task<List<AdComputer>> SearchAsync(ClaimsPrincipal actor, string? term, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            if (scope.IsEmpty)
                return new List<AdComputer>();

            List<AdComputer> found;
            if (scope.IsGlobal)
            {
                found = await _ad.GetAllComputersAsync(term, ct);
            }
            else
            {
                found = new List<AdComputer>();
                foreach (var root in scope.Roots)
                    found.AddRange(await _ad.GetComputersByOuAsync(root.ToString(), term, ct));
            }

            return found
                .Where(c => scope.Contains(c.DistinguishedName))
                .GroupBy(c => c.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        public async Task<AdComputer?> GetAsync(ClaimsPrincipal actor, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
                return null;

            var scope = await _scopes.ResolveAsync(actor, ct);
            var computer = await _ad.GetComputerByNameAsync(name.Trim(), ct);
            if (computer == null)
                return null;

            if (!scope.Contains(computer.DistinguishedName))
            {
                await _audit.RecordAsync(new AuditRecord("ad.computer.read")
                {
                    ResourceType = "AdComputer",
                    ResourceId = computer.DistinguishedName,
                    ResourceName = computer.Name,
                    Scope = scope.Describe(),
                    Success = false,
                    ErrorCode = "denied",
                    ErrorMessage = "Hedef bilgisayar kapsam dışında.",
                }, ct);
                return null;
            }

            return computer;
        }

        public async Task<(AdOperationResult Result, LapsResult? Laps)> RevealLapsAsync(ClaimsPrincipal actor, string name, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            var computer = string.IsNullOrWhiteSpace(name) ? null : await _ad.GetComputerByNameAsync(name.Trim(), ct);

            if (computer == null)
                return (AdOperationResult.NotFound(), null);

            var record = new AuditRecord(AuditActions.LapsRead)
            {
                ResourceType = "AdComputer",
                ResourceId = computer.DistinguishedName,
                ResourceName = computer.Name,
                Scope = scope.Describe(),
            };

            if (!scope.Contains(computer.DistinguishedName))
            {
                await _audit.RecordAsync(record with { Success = false, ErrorCode = "denied", ErrorMessage = "Hedef bilgisayar kapsam dışında." }, ct);
                return (AdOperationResult.Denied("Bu bilgisayar için yetkiniz yok."), null);
            }

            var laps = await _ad.ReadLapsAsync(computer.DistinguishedName, ct);

            // Parola ASLA audit'e yazılmaz: yalnızca kaynağı ve sonucun varlığı.
            await _audit.RecordAsync(record with
            {
                Success = laps.HasPassword,
                ErrorCode = laps.HasPassword ? null : laps.Source == LapsSource.WindowsLapsEncrypted ? "encrypted" : "not_found",
                ErrorMessage = laps.HasPassword ? null : laps.Message,
                Metadata = new { Source = laps.Source.ToString(), laps.AccountName, laps.ExpiresUtc },
            }, ct);

            return (laps.HasPassword ? AdOperationResult.Ok() : AdOperationResult.NotFound(), laps);
        }
    }
}

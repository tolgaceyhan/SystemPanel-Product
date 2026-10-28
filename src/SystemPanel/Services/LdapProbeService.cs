using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Configuration;

namespace SystemPanel.Services
{
    /// <summary>
    /// Bağımsız LDAP yardımcı servisi: mevcut IAdUserService/AdUserService ile ÇAKIŞMAZ.
    /// Sağlık kontrolü ve basit aramalar için kullanılır.
    /// </summary>
    public class LdapProbeService : IDisposable
    {
        private readonly ILogger<LdapProbeService> _logger;
        private readonly LdapOptions _options;

        public LdapProbeService(ILogger<LdapProbeService> logger, IOptions<LdapOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public DirectoryEntry GetEntry(string dnOrLdapPath)
        {
            // Accept DN (CN=...,DC=...) or full LDAP path
            var path = dnOrLdapPath.StartsWith("LDAP", StringComparison.OrdinalIgnoreCase) ||
                       dnOrLdapPath.StartsWith("LDAPS", StringComparison.OrdinalIgnoreCase)
                       ? dnOrLdapPath
                       : _options.BuildLdapPath(dnOrLdapPath);

            var username = NormalizeUsername(_options.BindUser);
            var password = _options.GetPasswordFromEnvironmentIfNeeded();

            // Use secure auth; if UseSsl is true, ensure encryption by transport.
            var authTypes = AuthenticationTypes.Secure;
            // Optional: add Signing/Sealing when domain policy requires it
            // authTypes |= AuthenticationTypes.Signing | AuthenticationTypes.Sealing;

            var entry = new DirectoryEntry(path, username, password, authTypes);

            _logger.LogDebug("Creating DirectoryEntry for path {Path}, user format {User}", path, GetUserFormat(username));
            return entry;
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                var retries = Math.Max(0, _options.MaxRetries);
                for (int attempt = 0; attempt <= retries; attempt++)
                {
                    try
                    {
                        using var entry = GetEntry(_options.BaseDn);
                        // Touch NativeObject to force bind
                        var _ = entry.NativeObject;
                        _logger.LogInformation("LDAP bind success on attempt {Attempt}", attempt + 1);
                        return true;
                    }
                    catch (Exception ex) when (IsTransient(ex) && attempt < retries)
                    {
                        _logger.LogWarning(ex, "Transient LDAP error on attempt {Attempt}. Retrying...", attempt + 1);
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LDAP Ping failed");
            }
            return false;
        }

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string ldapFilter,
            string? searchBaseDn = null,
            string[]? propertiesToLoad = null,
            int? sizeLimit = null,
            CancellationToken ct = default)
        {
            var results = new List<SearchResult>();
            var retries = Math.Max(0, _options.MaxRetries);

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var entry = GetEntry(searchBaseDn ?? _options.BaseDn);
                    using var ds = new DirectorySearcher(entry)
                    {
                        Filter = ldapFilter,
                        PageSize = 1000,
                        // Timeouts
                        ClientTimeout = TimeSpan.FromSeconds(_options.ClientTimeoutSeconds),
                        ServerTimeLimit = TimeSpan.FromSeconds(_options.ServerTimeLimitSeconds),
                        ReferralChasing = ParseReferral(_options.ReferralChasing),
                        SizeLimit = sizeLimit ?? 0
                    };

                    if (propertiesToLoad != null && propertiesToLoad.Length > 0)
                    {
                        ds.PropertiesToLoad.Clear();
                        foreach (var p in propertiesToLoad) ds.PropertiesToLoad.Add(p);
                    }

                    _logger.LogDebug("LDAP search: base={Base}, filter={Filter}, sizeLimit={SizeLimit}, referral={Referral}",
                        entry.Path, ldapFilter, ds.SizeLimit, ds.ReferralChasing);

                    var srch = await Task.Run(() => ds.FindAll(), ct);
                    foreach (SearchResult sr in srch)
                        results.Add(sr);

                    _logger.LogInformation("LDAP search returned {Count} results", results.Count);
                    return results;
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < retries)
                {
                    _logger.LogWarning(ex, "Transient LDAP error on attempt {Attempt}. Retrying...", attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LDAP search failed (filter: {Filter})", ldapFilter);
                    throw;
                }
            }

            return results;
        }

        private static ReferralChasingOption ParseReferral(string value) =>
            value?.ToLowerInvariant() switch
            {
                "none" => ReferralChasingOption.None,
                "subordinate" => ReferralChasingOption.Subordinate,
                "external" => ReferralChasingOption.External,
                _ => ReferralChasingOption.All,
            };

        private static bool IsTransient(Exception ex)
        {
            // Basic heuristic: network timeouts, server unavailable, etc.
            var msg = ex.ToString().ToLowerInvariant();
            return msg.Contains("timeout") || msg.Contains("unavailable") || msg.Contains("server is not operational");
        }

        private static string NormalizeUsername(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return user;
            // Prefer UPN format if contains '@'
            if (user.Contains("@")) return user;
            // Fallback: leave as-is (DOMAIN\\user handled by DirectoryEntry too)
            return user;
        }

        private static string GetUserFormat(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return "empty";
            if (user.Contains("@")) return "UPN";
            if (user.Contains("\\")) return "DOMAIN\\\\user";
            return "SAMAccountName";
        }

        public void Dispose() { }
    }
}

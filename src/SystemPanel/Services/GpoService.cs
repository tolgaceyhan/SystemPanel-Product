using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Configuration;
using SystemPanel.Services.Gpo;
using SystemPanel.ViewModels.Gpo;

namespace SystemPanel.Services
{
    public class GpoService : IGpoService
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private readonly ILogger<GpoService> _logger;
        private readonly IPowerShellScriptRunner _ps;
        private readonly GpoOptions _options;
        private readonly LdapOptions _ldap;

        public GpoService(
            ILogger<GpoService> logger,
            IPowerShellScriptRunner ps,
            IOptions<GpoOptions> options,
            IOptions<LdapOptions> ldap)
        {
            _logger = logger;
            _ps = ps;
            _options = options.Value;
            _ldap = ldap.Value;
        }

        // ===============================
        // OKUMA
        // ===============================

        public async Task<List<GpoViewModel>> GetAllGposAsync(CancellationToken ct = default)
        {
            var result = await RunAsync("GetGpoList.ps1", new Dictionary<string, string>(), ct);
            if (result == null) return new();

            try
            {
                return JsonSerializer.Deserialize<List<GpoViewModel>>(result.StdOut, Json) ?? new();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "GPO listesi ayrıştırılamadı.");
                return new();
            }
        }

        public async Task<GpoViewModel?> GetGpoByIdAsync(string id, CancellationToken ct = default)
        {
            if (!GpoInput.TryNormalizeGuid(id, out var guid))
                return null;

            var result = await RunAsync("GetGpoDetails.ps1", new Dictionary<string, string> { ["Guid"] = guid }, ct);
            if (result == null) return null;

            try
            {
                return JsonSerializer.Deserialize<GpoViewModel>(result.StdOut, Json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "GPO detayı ayrıştırılamadı: {Guid}", guid);
                return null;
            }
        }

        public async Task<List<GpoViewModel>> GetGposByOuAsync(string ouDn, CancellationToken ct = default)
        {
            if (!GpoInput.TryNormalizeOuDn(ouDn, _ldap.BaseDn, out var dn))
                return new();

            var result = await RunAsync("GetOuGpoLinks.ps1", new Dictionary<string, string> { ["OuDn"] = dn }, ct);
            if (result == null) return new();

            HashSet<string> ids;
            try
            {
                using var doc = JsonDocument.Parse(result.StdOut);
                ids = doc.RootElement.EnumerateArray()
                    .Select(e => e.GetProperty("GpoId").GetString() ?? "")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                _logger.LogError(ex, "OU GPO bağlantıları ayrıştırılamadı.");
                return new();
            }

            var all = await GetAllGposAsync(ct);
            return all.Where(g => ids.Contains(g.Id)).ToList();
        }

        public async Task<GpoOuTreeViewModel> GetOuGpoTreeAsync()
        {
            return await Task.Run(() =>
            {
                using var entry = CreateEntry(_ldap.BaseDn);
                return BuildOuTree(entry);
            });
        }

        // ===============================
        // DEĞİŞTİREN İŞLEMLER
        // ===============================

        public Task<GpoOperationResult> LinkGpoToOuAsync(string gpoId, string ouDn, CancellationToken ct = default)
            => RunGuidAndOuAsync("LinkGpo.ps1", gpoId, ouDn, ct);

        public Task<GpoOperationResult> UnlinkGpoFromOuAsync(string gpoId, string ouDn, CancellationToken ct = default)
            => RunGuidAndOuAsync("UnlinkGpo.ps1", gpoId, ouDn, ct);

        public async Task<GpoOperationResult> SetGpoEnabledAsync(string gpoId, bool isEnabled, CancellationToken ct = default)
        {
            if (!GpoInput.TryNormalizeGuid(gpoId, out var guid))
                return GpoOperationResult.Fail("Geçersiz GPO kimliği.");

            return await RunOperationAsync("SetGpoStatus.ps1", new Dictionary<string, string>
            {
                ["Guid"] = guid,
                ["Status"] = isEnabled ? "AllSettingsEnabled" : "AllSettingsDisabled",
            }, ct);
        }

        public async Task<GpoOperationResult> BackupGpoAsync(string gpoId, CancellationToken ct = default)
        {
            if (!GpoInput.TryNormalizeGuid(gpoId, out var guid))
                return GpoOperationResult.Fail("Geçersiz GPO kimliği.");

            var root = string.IsNullOrWhiteSpace(_options.BackupRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "GpoBackups")
                : _options.BackupRoot;

            var path = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + guid);

            var result = await RunOperationAsync("BackupGpo.ps1", new Dictionary<string, string>
            {
                ["Guid"] = guid,
                ["Path"] = path,
            }, ct);

            return result.Success ? GpoOperationResult.Ok(path) : result;
        }

        public async Task<GpoOperationResult> DeleteGpoAsync(string gpoId, CancellationToken ct = default)
        {
            if (!_options.AllowDelete)
                return GpoOperationResult.Fail("GPO silme bu kurulumda devre dışı (Gpo:AllowDelete).");

            if (!GpoInput.TryNormalizeGuid(gpoId, out var guid))
                return GpoOperationResult.Fail("Geçersiz GPO kimliği.");

            return await RunOperationAsync("DeleteGpo.ps1", new Dictionary<string, string> { ["Guid"] = guid }, ct);
        }

        public async Task<GpoOperationResult> ImportGpoAsync(IFormFile backupZip, string backupGpoName, string targetName, CancellationToken ct = default)
        {
            if (!_options.AllowImport)
                return GpoOperationResult.Fail("GPO içe aktarma bu kurulumda devre dışı (Gpo:AllowImport).");

            if (backupZip == null || backupZip.Length == 0 || backupZip.Length > _options.MaxImportBytes)
                return GpoOperationResult.Fail("Yedek dosyası boş veya izin verilen boyuttan büyük.");

            if (!string.Equals(Path.GetExtension(backupZip.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
                return GpoOperationResult.Fail("Yedek, Backup-GPO klasörünün .zip arşivi olmalıdır.");

            if (!GpoInput.IsValidDisplayName(backupGpoName) || !GpoInput.IsValidDisplayName(targetName))
                return GpoOperationResult.Fail("Geçersiz GPO adı.");

            var temp = Path.Combine(Path.GetTempPath(), "SystemPanelGpoImport", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                await using (var stream = backupZip.OpenReadStream())
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    SafeExtract(zip, temp);
                }

                return await RunOperationAsync("ImportGpo.ps1", new Dictionary<string, string>
                {
                    ["Path"] = temp,
                    ["BackupGpoName"] = backupGpoName.Trim(),
                    ["TargetName"] = targetName.Trim(),
                }, ct);
            }
            catch (InvalidDataException)
            {
                return GpoOperationResult.Fail("Yedek arşivi geçersiz.");
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { /* geçici klasör */ }
            }
        }

        /// <summary>Zip-slip korumalı açma: hiçbir giriş hedef klasörün dışına yazılamaz.</summary>
        internal static void SafeExtract(ZipArchive zip, string destination)
        {
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Arşiv hedef klasör dışına yazmaya çalışıyor.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
            }
        }

        // ===============================
        // YARDIMCILAR
        // ===============================

        private async Task<GpoOperationResult> RunGuidAndOuAsync(string script, string gpoId, string ouDn, CancellationToken ct)
        {
            if (!GpoInput.TryNormalizeGuid(gpoId, out var guid))
                return GpoOperationResult.Fail("Geçersiz GPO kimliği.");

            if (!GpoInput.TryNormalizeOuDn(ouDn, _ldap.BaseDn, out var dn))
                return GpoOperationResult.Fail("Geçersiz veya domain dışı OU.");

            return await RunOperationAsync(script, new Dictionary<string, string> { ["Guid"] = guid, ["OuDn"] = dn }, ct);
        }

        private async Task<GpoOperationResult> RunOperationAsync(string script, Dictionary<string, string> parameters, CancellationToken ct)
        {
            PowerShellScriptResult result;
            try
            {
                result = await _ps.RunAsync(script, parameters, ct);
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or System.ComponentModel.Win32Exception)
            {
                _logger.LogError(ex, "GPO betiği çalıştırılamadı: {Script}", script);
                return GpoOperationResult.Fail("GPO işlemi başlatılamadı.");
            }

            if (result.Succeeded)
                return GpoOperationResult.Ok();

            var error = ExtractError(result);
            _logger.LogWarning("GPO betiği başarısız: {Script} exit={ExitCode} timeout={TimedOut} error={Error}",
                script, result.ExitCode, result.TimedOut, error);
            return GpoOperationResult.Fail(result.TimedOut ? "GPO işlemi zaman aşımına uğradı." : error);
        }

        private async Task<PowerShellScriptResult?> RunAsync(string script, Dictionary<string, string> parameters, CancellationToken ct)
        {
            try
            {
                var result = await _ps.RunAsync(script, parameters, ct);
                if (result.Succeeded)
                    return result;

                _logger.LogWarning("GPO betiği başarısız: {Script} exit={ExitCode} error={Error}", script, result.ExitCode, ExtractError(result));
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or System.ComponentModel.Win32Exception)
            {
                _logger.LogError(ex, "GPO betiği çalıştırılamadı: {Script}", script);
            }

            return null;
        }

        internal static string ExtractError(PowerShellScriptResult result)
        {
            try
            {
                using var doc = JsonDocument.Parse(result.StdOut);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var e) &&
                    e.GetString() is { Length: > 0 } msg)
                {
                    return msg.Length > 500 ? msg[..500] : msg;
                }
            }
            catch (JsonException) { }

            var err = (result.StdErr ?? string.Empty).Trim();
            return string.IsNullOrEmpty(err) ? "GPO işlemi başarısız." : (err.Length > 500 ? err[..500] : err);
        }

        private DirectoryEntry CreateEntry(string dn)
        {
            var path = _ldap.BuildLdapPath(dn);
            var user = string.IsNullOrWhiteSpace(_ldap.BindUser) ? null : _ldap.BindUser;
            var pass = user == null ? null : _ldap.GetPasswordFromEnvironmentIfNeeded();
            return new DirectoryEntry(path, user, pass, AuthenticationTypes.Secure);
        }

        private GpoOuTreeViewModel BuildOuTree(DirectoryEntry entry)
        {
            var dn = entry.Properties["distinguishedName"]?.Value?.ToString();
            var name = entry.Properties["name"]?.Value?.ToString() ?? "OU";

            var result = new GpoOuTreeViewModel
            {
                OuName = name,
                DistinguishedName = dn,
                LinkedGpos = new()
            };

            if (entry.Properties["gPLink"]?.Value is string raw)
                result.LinkedGpos.AddRange(ParseGpLink(raw));

            foreach (DirectoryEntry child in entry.Children)
            {
                using (child)
                {
                    if (child.SchemaClassName == "organizationalUnit")
                        result.ChildOus.Add(BuildOuTree(child));
                }
            }

            return result;
        }

        private List<string> ParseGpLink(string gplink)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(gplink)) return list;

            foreach (var block in gplink.Split(new[] { "][" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = block.Replace("[", "").Replace("]", "");
                var path = clean.Split(';')[0];
                if (!path.Contains("CN=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = GetGpoDisplayName(path);
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(name);
            }

            return list;
        }

        private string? GetGpoDisplayName(string ldapPath)
        {
            try
            {
                var dn = ldapPath.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase) ? ldapPath[7..] : ldapPath;
                using var de = CreateEntry(dn);
                return de.Properties["displayName"]?.Value?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GPO adı okunamadı.");
                return null;
            }
        }
    }
}

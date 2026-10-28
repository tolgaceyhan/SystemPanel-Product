using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SystemPanel.ViewModels.Gpo;

namespace SystemPanel.Services
{
    public sealed record GpoOperationResult(bool Success, string? Error = null, string? Detail = null)
    {
        public static GpoOperationResult Ok(string? detail = null) => new(true, null, detail);
        public static GpoOperationResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// GPO işlemleri. Tüm GUID/DN/ad girdileri doğrulanır ve PowerShell'e yalnızca parametre
    /// olarak geçirilir. Yetki ve audit controller'da (GpoController) uygulanır.
    /// </summary>
    public interface IGpoService
    {
        Task<List<GpoViewModel>> GetAllGposAsync(CancellationToken ct = default);
        Task<GpoViewModel?> GetGpoByIdAsync(string id, CancellationToken ct = default);
        Task<List<GpoViewModel>> GetGposByOuAsync(string ouDn, CancellationToken ct = default);
        Task<GpoOuTreeViewModel> GetOuGpoTreeAsync();

        Task<GpoOperationResult> LinkGpoToOuAsync(string gpoId, string ouDn, CancellationToken ct = default);
        Task<GpoOperationResult> UnlinkGpoFromOuAsync(string gpoId, string ouDn, CancellationToken ct = default);
        Task<GpoOperationResult> SetGpoEnabledAsync(string gpoId, bool isEnabled, CancellationToken ct = default);
        Task<GpoOperationResult> BackupGpoAsync(string gpoId, CancellationToken ct = default);
        Task<GpoOperationResult> DeleteGpoAsync(string gpoId, CancellationToken ct = default);
        Task<GpoOperationResult> ImportGpoAsync(IFormFile backupZip, string backupGpoName, string targetName, CancellationToken ct = default);
    }
}

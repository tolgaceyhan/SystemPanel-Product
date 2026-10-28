using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Enums;
using SystemPanel.Models;
using SystemPanel.ViewModels.AdUser;

namespace SystemPanel.Services
{
    public interface IAdUserService : IDisposable
    {
        // ========== DN & OU Helpers ==========
        string ResolveBaseDn();
        IEnumerable<string> EnumerateDomainBaseDns();
        string BuildOuDn(string provinceOrDn);
        Task<string> GetDistinguishedName(string samOrUpnOrDn);
        bool IsSameDn(string a, string b);
        bool IsUnderDn(string child, string root);

        // ========== Authorization (eski; yeni kod IOrganizationalScopeResolver kullanır) ==========
        Task<HashSet<string>> GetAuthorizedOuScopesForUserAsync(string samOrUserDn);
        Task<bool> IsUserAuthorizedForScopeAsync(string samOrUserDn, string requestedScopeDn);

        // ========== Users Search ==========
        Task<IEnumerable<AdUser>> GetAdUsers(string searchTerm);
        Task<List<AdUser>> GetUsersByProvinceAsync(string provinceOrDn);
        Task<List<AdUser>> GetAdUsersInOuAsync(string searchTerm, string provinceOrDn, CancellationToken ct = default);

        // ========== OU ==========
        Task<List<OrganizationalUnit>> GetOrganizationalUnitsByNameAsync(string name, CancellationToken ct = default);
        Task<OuTreeDto> GetOuTreeAsync(CancellationToken ct = default);
        Task<LocalAdminScopeViewModel> GetLocalAdminScopeAsync(string ou, CancellationToken ct = default);

        // ========== Users CRUD ==========
        Task<IEnumerable<AdUser>> GetAllAdUsersAsync();
        Task<AdUser?> GetUserByIdAsync(string id);
        Task<bool> CreateUserAsync(AdUser user, string password);
        Task<bool> UpdateUserAsync(string id, EditAdUserViewModel model);
        Task<bool> DeleteUserAsync(string id);

        /// <summary>Yerleşik ayrıcalıklı veya yapılandırmada korunan grup mu? (üyelik değiştirilemez)</summary>
        bool IsProtectedGroup(string groupDnOrName);

        // ========== Groups (low-level by DN) ==========
        Task<bool> AddUserToGroupByDn(string userDn, string groupDn);
        Task<bool> RemoveUserFromGroupByDn(string userDn, string groupDn);

        Task<List<string>> GetUserGroupsAsync(string userDn);
        Task<List<AdGroup>> GetAllSecurityGroupsAsync(string search = "");

        Task<bool> IsUserInGroupByDnAsync(string userDn, string groupDn);
        Task<bool> IsUserInGroupByNameAsync(string userDn, string groupName);

        // ========== Password / Lock ==========
        Task ResetPasswordWithServiceAccount(string targetDn, string newPassword);
        Task ForceChangeAtNextLogon(string targetDn);
        Task<bool> UnlockAccountAsync(string userDn);
        Task<bool> IsUserLockedAsync(string userDn);
        Task<bool> ResetPasswordAndUnlockAsync(string userDn, string newPassword);
        Task<bool> ResetPasswordManualAsync(string userDn, string newPassword);
        Task<string> ResetPasswordAsync(string userDn, string newPassword);
        Task SendSmsPassword(string userDn, string newPassword);

        Task<bool> DisableUserAsync(string userDn);
        Task<bool> EnableUserAsync(string userDn);

        // ========== Misc ==========
        Task<string> GetPhoneNumber(string samAccountName);

        // ========== Computers & LAPS ==========
        Task<string> GetComputerDistinguishedName(string computerName);
        Task<List<AdComputer>> GetComputersByProvinceAsync(string provinceOrDn);
        Task<List<AdServer>> GetDistributionServersByProvince(string provinceDn);

        // ========== OU Move ==========
        Task<bool> MoveUserToOuAsync(string userDn, string targetOuDn);

        // ========== Bulk CSV ==========
        Task<List<string>> BulkCreateUsersFromCsvAsync(Stream csvStream);
        Task<List<string>> BulkDeleteUsersFromCsvAsync(Stream csvStream);

        // ========== LDAP Helpers ==========
        DirectoryEntry GetEntry(string dnOrLdapPath);
        Task<bool> PingAsync(CancellationToken ct = default);

        Task<IReadOnlyList<SearchResult>> SearchAsync(
            string ldapFilter,
            string? searchBaseDn = null,
            string[]? propertiesToLoad = null,
            int? sizeLimit = null,
            CancellationToken ct = default);
    }
}

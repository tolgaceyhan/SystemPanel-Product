using System;
using System.Collections.Generic;
using System.Linq;
using P = SystemPanel.Security.Permissions;

namespace SystemPanel.Security
{
    /// <summary>
    /// RolePermissions tablosu boşsa (ilk kurulum) veya veritabanına ulaşılamıyorsa
    /// kullanılan varsayılan rol → izin eşlemesi. Rol adları yalnızca burada ve
    /// yapılandırmada (RoleMapping) geçer; controller'lar izin kullanır.
    ///
    /// Eşleme, Sprint 0 öncesindeki [Authorize(Roles=...)] davranışını korumak üzere
    /// çıkarılmıştır (bkz. docs/AUTHORIZATION.md "Geçiş").
    /// </summary>
    public static class DefaultRolePermissions
    {
        public const string Admin = "Admin";
        public const string LocalAdmin = "LocalAdmin";
        public const string Operator = "Operator";
        public const string Helpdesk = "Helpdesk";
        public const string Monitoring = "Monitoring";

        public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Map =
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [Admin] = P.All.ToArray(),

                [LocalAdmin] = new[]
                {
                    P.DashboardView,
                    P.ServersView, P.ServersManage,
                    P.AdUsersView, P.AdUsersEdit, P.AdUsersDisable, P.AdUsersPasswordReset,
                    P.AdUsersUnlock, P.AdUsersMove, P.AdGroupsManage,
                    P.AdComputersView, P.LapsRead,
                    P.SccmView, P.SccmManage,
                    P.AnnouncementsView,
                    P.MonitoringView,
                },

                [Operator] = new[]
                {
                    P.DashboardView,
                    P.AdUsersView, P.AdUsersEdit, P.AdUsersDisable, P.AdUsersPasswordReset,
                    P.AdUsersUnlock, P.AdUsersMove, P.AdGroupsManage,
                },

                [Helpdesk] = new[]
                {
                    P.DashboardView,
                    P.AdUsersView, P.AdUsersCreate, P.AdUsersEdit, P.AdUsersDisable,
                    P.AdUsersPasswordReset, P.AdUsersUnlock, P.AdUsersMove, P.AdGroupsManage,
                },

                [Monitoring] = new[]
                {
                    P.DashboardView,
                    P.MonitoringView,
                },
            };
    }
}

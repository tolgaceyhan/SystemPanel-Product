using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SystemPanel.Security
{
    /// <summary>
    /// Uygulamadaki tüm izinlerin tek kaynağı (permission catalog).
    /// Yetkilendirme rol adlarına göre değil, bu izinlere göre yapılır; roller
    /// izinlere <see cref="DefaultRolePermissions"/> / RolePermissions tablosu ile eşlenir.
    /// Yeni bir izin eklerken buraya ekleyin ve docs/AUTHORIZATION.md'yi güncelleyin.
    /// </summary>
    public static class Permissions
    {
        public const string DashboardView = "dashboard.view";
        public const string DashboardManage = "dashboard.manage";

        public const string ServersView = "servers.view";
        public const string ServersManage = "servers.manage";
        public const string ServersServicesControl = "servers.services.control";

        public const string AdUsersView = "ad.users.view";
        public const string AdUsersCreate = "ad.users.create";
        public const string AdUsersEdit = "ad.users.edit";
        public const string AdUsersDisable = "ad.users.disable";
        public const string AdUsersDelete = "ad.users.delete";
        public const string AdUsersPasswordReset = "ad.users.password.reset";
        public const string AdUsersUnlock = "ad.users.unlock";
        public const string AdUsersMove = "ad.users.move";
        public const string AdGroupsManage = "ad.groups.manage";

        public const string AdComputersView = "ad.computers.view";
        public const string AdComputersManage = "ad.computers.manage";
        public const string LapsRead = "laps.read";

        public const string GpoView = "gpo.view";
        public const string GpoCreate = "gpo.create";
        public const string GpoEdit = "gpo.edit";
        public const string GpoLink = "gpo.link";
        public const string GpoBackup = "gpo.backup";
        public const string GpoDelete = "gpo.delete";

        public const string SccmView = "sccm.view";
        public const string SccmManage = "sccm.manage";

        public const string FilesView = "files.view";
        public const string FilesUpload = "files.upload";
        public const string FilesDelete = "files.delete";

        public const string AnnouncementsView = "announcements.view";
        public const string AnnouncementsManage = "announcements.manage";

        public const string MonitoringView = "monitoring.view";
        public const string MonitoringManage = "monitoring.manage";

        public const string SmsSend = "sms.send";
        public const string SmsBulkSend = "sms.bulk";

        public const string AutomationView = "automation.view";
        public const string AutomationRun = "automation.run";
        public const string AutomationApprove = "automation.approve";
        public const string AutomationManage = "automation.manage";

        public const string AuditView = "audit.view";
        public const string LogsView = "logs.view";
        public const string LogsManage = "logs.manage";

        public const string SettingsView = "settings.view";
        public const string SettingsManage = "settings.manage";
        public const string UsersManage = "users.manage";

        /// <summary>
        /// Organizasyon geneli kapsam. Bu izne sahip olmayan kullanıcıların AD/sunucu
        /// işlemleri <see cref="Scope.OrganizationalScope"/> ile sınırlandırılır.
        /// </summary>
        public const string ScopeAll = "scope.all";

        private static readonly IReadOnlyList<string> _all = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(p => p)
            .ToList();

        public static IReadOnlyList<string> All => _all;

        public static bool IsKnown(string permission) => _all.Contains(permission);

        /// <summary>Yönetim ekranı için Türkçe açıklamalar.</summary>
        public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            [DashboardView] = "Ana sayfa / operasyon özeti",
            [DashboardManage] = "NOC dashboard ekran ve panel yönetimi",
            [ServersView] = "Sunucu envanteri ve metrikleri görüntüleme",
            [ServersManage] = "Sunucu envanterine ekleme / düzenleme",
            [ServersServicesControl] = "Sunucu servislerini başlatma / durdurma",
            [AdUsersView] = "AD kullanıcı arama ve detay",
            [AdUsersCreate] = "AD kullanıcı oluşturma",
            [AdUsersEdit] = "AD kullanıcı bilgilerini düzenleme",
            [AdUsersDisable] = "AD kullanıcıyı etkinleştirme / devre dışı bırakma",
            [AdUsersDelete] = "AD kullanıcı silme",
            [AdUsersPasswordReset] = "AD kullanıcı parola sıfırlama",
            [AdUsersUnlock] = "AD kullanıcı kilidini açma",
            [AdUsersMove] = "AD kullanıcıyı OU'ya taşıma",
            [AdGroupsManage] = "AD grup üyeliği ekleme / çıkarma",
            [AdComputersView] = "AD bilgisayar arama ve detay",
            [AdComputersManage] = "AD bilgisayar işlemleri",
            [LapsRead] = "LAPS parolası görüntüleme (her erişim audit'lenir)",
            [GpoView] = "GPO envanteri görüntüleme",
            [GpoCreate] = "GPO oluşturma / içe aktarma",
            [GpoEdit] = "GPO durumunu değiştirme",
            [GpoLink] = "GPO'yu OU'ya bağlama / bağı kaldırma",
            [GpoBackup] = "GPO yedekleme",
            [GpoDelete] = "GPO silme",
            [SccmView] = "SCCM / DP görüntüleme",
            [SccmManage] = "SCCM DP servis kontrolü",
            [FilesView] = "Dosya listeleme / indirme",
            [FilesUpload] = "Dosya yükleme",
            [FilesDelete] = "Dosya silme",
            [AnnouncementsView] = "Duyuruları görüntüleme",
            [AnnouncementsManage] = "Duyuru ekleme / düzenleme / silme",
            [MonitoringView] = "İzleme, harita ve alarmlar",
            [MonitoringManage] = "Agent kaydı ve izleme ayarları",
            [SmsSend] = "Tekil SMS gönderme",
            [SmsBulkSend] = "Toplu SMS gönderme (grup / OU / tüm kullanıcılar)",
            [AutomationView] = "Otomasyon kataloğunu ve çalıştırmaları görüntüleme",
            [AutomationRun] = "Runbook çalıştırma talebi oluşturma",
            [AutomationApprove] = "Başkasının runbook talebini onaylama/reddetme (dört göz)",
            [AutomationManage] = "Runbook paketi içe aktarma, imzalama anahtarlarını yönetme",
            [AuditView] = "Güvenlik audit kayıtlarını görüntüleme",
            [LogsView] = "Uygulama loglarını görüntüleme / dışa aktarma",
            [LogsManage] = "Uygulama loglarını silme",
            [SettingsView] = "Ayarları ve ayrıntılı sağlık durumunu görüntüleme",
            [SettingsManage] = "Ayarları ve rol-izin eşlemesini değiştirme",
            [UsersManage] = "Panel kullanıcıları ve rol ataması",
            [ScopeAll] = "Tüm organizasyon (il/birim kısıtı olmadan)",
        };
    }
}

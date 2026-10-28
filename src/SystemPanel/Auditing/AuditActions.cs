namespace SystemPanel.Auditing
{
    /// <summary>Audit "Action" değerleri. Arama/filtreleme için sabit tutun; yeniden adlandırmayın.</summary>
    public static class AuditActions
    {
        public const string LoginSucceeded = "auth.login.success";
        public const string LoginFailed = "auth.login.failure";
        public const string Logout = "auth.logout";
        public const string AccessDenied = "authz.denied";

        public const string AdUserCreate = "ad.user.create";
        public const string AdUserUpdate = "ad.user.update";
        public const string AdUserDelete = "ad.user.delete";
        public const string AdUserEnable = "ad.user.enable";
        public const string AdUserDisable = "ad.user.disable";
        public const string AdUserUnlock = "ad.user.unlock";
        public const string AdUserPasswordReset = "ad.user.password.reset";
        public const string AdUserMove = "ad.user.move";
        public const string AdGroupMemberAdd = "ad.group.member.add";
        public const string AdGroupMemberRemove = "ad.group.member.remove";

        public const string LapsRead = "laps.read";

        public const string GpoLink = "gpo.link";
        public const string GpoUnlink = "gpo.unlink";
        public const string GpoStatusChange = "gpo.status";
        public const string GpoBackup = "gpo.backup";
        public const string GpoDelete = "gpo.delete";
        public const string GpoImport = "gpo.import";

        public const string ServiceStart = "service.start";
        public const string ServiceStop = "service.stop";
        public const string ServiceRestart = "service.restart";

        public const string FileUpload = "file.upload";
        public const string FileDelete = "file.delete";

        public const string RolePermissionsUpdate = "authz.role-permissions.update";
        public const string IdentityUserCreate = "identity.user.create";
        public const string IdentityUserUpdate = "identity.user.update";
        public const string IdentityUserDelete = "identity.user.delete";
        public const string IdentityUserRoleAssign = "identity.user.role.assign";

        public const string LogsClear = "logs.clear";
        public const string SmsSend = "sms.send";

        public const string DashboardPanelCreate = "dashboard.panel.create";
        public const string DashboardPanelUpdate = "dashboard.panel.update";
        public const string DashboardPanelDelete = "dashboard.panel.delete";
        public const string DashboardPanelReorder = "dashboard.panel.reorder";

        public const string AgentRegister = "agent.register";
        public const string AgentKeyRotate = "agent.key.rotate";
        public const string AgentEnable = "agent.enable";
        public const string AgentDisable = "agent.disable";
        public const string AgentAuthFailed = "agent.auth.failure";

        public const string LicenseInstall = "license.install";
        public const string SetupCompleted = "setup.completed";
    }
}

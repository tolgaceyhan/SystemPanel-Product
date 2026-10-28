using System;
using System.IO;

namespace SystemPanel.Dashboard.Core
{
    /// <summary>
    /// Ayar, önbellek ve log yolları. Sabit sürücü harfi kullanılmaz (önceden E:\Dashboard idi);
    /// kök %ProgramData%\SystemPanel\Dashboard, SYSTEMPANEL_DASHBOARD_HOME ile değiştirilebilir.
    /// </summary>
    public sealed class DashboardPaths
    {
        public const string HomeEnvironmentVariable = "SYSTEMPANEL_DASHBOARD_HOME";

        public DashboardPaths(string root)
        {
            Root = root;
        }

        public string Root { get; }
        public string SettingsFile => Path.Combine(Root, "appsettings.json");
        public string ConfigCacheFile => Path.Combine(Root, "config-cache.json");
        public string LogDirectory => Path.Combine(Root, "logs");

        public static DashboardPaths Default()
        {
            var overridden = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
            var root = !string.IsNullOrWhiteSpace(overridden)
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "Dashboard");
            return new DashboardPaths(root);
        }
    }
}

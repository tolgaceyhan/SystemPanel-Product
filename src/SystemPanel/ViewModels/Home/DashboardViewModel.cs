using SystemPanel.Models;
using System.Collections.Generic;

namespace SystemPanel.ViewModels.Home
{
    public class DashboardViewModel
    {
        public int ActiveServiceCount { get; set; }
        public double AvgCpuUsage { get; set; }
        public double AvgRamUsage { get; set; }
        public double AvgDiskUsage { get; set; } // Ortalama disk kullanımı (%)
        public int ActiveWindowsServiceCount { get; set; }

        public int TotalAdUserCount { get; set; }       // AD kullanıcı toplamı
        public int TotalAlarmCount { get; set; }        // Tüm alarm sayısı
        public string LoggedInUserRole { get; set; } = "Rol Yok";    // Kullanıcının rolü

        public List<ServerMetricHistory> RecentMetrics { get; set; } = new();
        public List<AlarmLog> RecentAlarms { get; set; } = new();
        public int TotalServerCount { get; set; }
        public int OnlineServerCount { get; set; }
        public int OfflineServerCount { get; set; }


    }
}

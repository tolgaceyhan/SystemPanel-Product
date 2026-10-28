using SystemPanel.Models;

namespace SystemPanel.ViewModels.Alarms
{
    public class AlarmStatsViewModel
    {
        public int TotalAlarms { get; set; }
        public int CpuAlarms { get; set; }
        public int RamAlarms { get; set; }
        public int DiskAlarms { get; set; }

        public List<AlarmLog> Last10Alarms { get; set; } = new();
    }
}

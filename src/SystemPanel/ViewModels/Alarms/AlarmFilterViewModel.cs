using SystemPanel.Models;

namespace SystemPanel.ViewModels.Alarms
{
    public class AlarmFilterViewModel
    {
        public string? ServerName { get; set; }

        // "CPU", "RAM", "Disk", "Disk C:", "Disk D:" vb.
        public string? Type { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public List<AlarmLog> Results { get; set; } = new();
    }
}

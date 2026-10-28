using System;

namespace SystemPanel.Models
{
    public class ServerMetricHistory
    {
        public int Id { get; set; }
        public string ServerName { get; set; } = string.Empty;

        public string MetricType { get; set; } = string.Empty; // cpu, ram, disk
        public int MetricValue { get; set; }

        public string? Extra { get; set; } // Disk C:, vs
        public DateTime CreatedAt { get; set; }
    }

}

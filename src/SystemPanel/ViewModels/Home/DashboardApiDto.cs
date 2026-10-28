using System.Collections.Generic;

namespace SystemPanel.ViewModels.Home
{
    public class DashboardApiDto
    {
        public StatisticsDto? Statistics { get; set; }
        public MetricsDto Metrics { get; set; } = new();
        public List<AlarmDto> Alarms { get; set; } = new();
        public List<DpServerDto> DpServers { get; set; } = new();
    }

    public class StatisticsDto
    {
        public int TotalUsers { get; set; }
        public int ActiveSessions { get; set; }
        public string SystemHealth { get; set; } = string.Empty;
    }

    public class MetricsDto
    {
        public List<MetricPointDto> Cpu { get; set; } = new();
        public List<MetricPointDto> Ram { get; set; } = new();
    }

    public class MetricPointDto
    {
        public string Time { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class AlarmDto
    {
        public string Server { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
    }

    public class DpServerDto
    {
        public string ServerName { get; set; } = string.Empty;
        public string Status { get; set; } = "Healthy";
        public double Cpu { get; set; }
        public double Ram { get; set; }
    }
}

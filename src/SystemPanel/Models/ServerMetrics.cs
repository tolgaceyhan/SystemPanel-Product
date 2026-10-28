namespace SystemPanel.Models
{
    public class ServerMetrics
    {
        public string ServerName { get; set; }
        public double CpuUsage { get; set; }
        public double RamUsage { get; set; }
        public double DiskUsage { get; set; }
    }
}

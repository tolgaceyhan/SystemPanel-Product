namespace SystemPanel.Models.Dto.View;

public class ServerMetricDto
{
    public string ServerName { get; set; } = string.Empty;

    public int CpuUsage { get; set; }
    public int RamUsage { get; set; }
    public int DiskUsage { get; set; }

    public int ServicesRunning { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}

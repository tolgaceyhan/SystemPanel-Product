namespace SystemPanel.Models;

public class LatestServerMetrics
{
    public int Id { get; set; }

    public string ServerName { get; set; } = string.Empty;

    // 🔥 YENİ
    public string Province { get; set; } = string.Empty;

    public string Ou { get; set; } = string.Empty;

    public int CpuUsage { get; set; }
    public int RamUsage { get; set; }

    // 🔥 YENİ
    public int DiskUsage { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}


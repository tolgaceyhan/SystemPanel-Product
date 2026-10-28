namespace SystemPanel.ViewModels.Server;

public class ServerSummaryViewModel
{
    public string ServerName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTime? LastUpdated { get; set; }

    public int CpuUsage { get; set; }
    public int RamUsage { get; set; }

    // ✅ read-only DiskUsage yerine bunu kullan
    public Dictionary<string, int> DiskUsages { get; set; } = new();

    public string Status { get; set; } = "Unknown";
}

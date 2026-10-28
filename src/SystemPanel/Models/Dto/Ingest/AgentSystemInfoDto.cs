namespace SystemPanel.Models.Dto.Ingest;

public class AgentSystemInfoDto
{
    public string OsName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = "1.0.0";
}

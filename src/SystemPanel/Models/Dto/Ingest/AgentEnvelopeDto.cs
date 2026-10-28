using System.Text.Json;

namespace SystemPanel.Models.Dto.Ingest;

public class AgentEnvelopeDto
{
    public string AgentId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
    public DateTime CollectedAtUtc { get; set; }

    public AgentSystemInfoDto SystemInfo { get; set; } = new();
    public List<MetricResultDto> Metrics { get; set; } = new();
}

using System.Text.Json;

namespace SystemPanel.Models.Dto.Ingest;

public class MetricResultDto
{
    public string Type { get; set; } = string.Empty;
    public JsonElement Data { get; set; }
}

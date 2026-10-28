namespace SystemPanel.Contracts.Agents;

/// <summary>
/// Agent API sabitleri. Sürüm yolun parçasıdır (/api/v1); geriye uyumsuz değişiklikte v2 açılır.
/// </summary>
public static class AgentApi
{
    public const string HeartbeatPath = "api/v1/agents/heartbeat";
    public const string MetricsPath = "api/v1/agents/metrics";

    /// <summary>Agent'a atanmış sıradaki runbook işi (ADR-002 Faz 2). İş yoksa 204.</summary>
    public const string NextCommandPath = "api/v1/agents/commands/next";

    /// <summary>İş sonucu: <c>api/v1/agents/commands/{runId}/result</c>.</summary>
    public const string CommandResultPathFormat = "api/v1/agents/commands/{0}/result";

    /// <summary>Agent kimliği (GUID). Kayıt sırasında SystemPanel tarafından verilir.</summary>
    public const string AgentIdHeader = "X-Agent-Id";

    /// <summary>Agent gizli anahtarı. Sunucuda yalnızca SHA-256 özeti saklanır; yalnızca HTTPS üzerinden gönderilmelidir.</summary>
    public const string AgentKeyHeader = "X-Agent-Key";

    /// <summary>Tek istekte kabul edilen en fazla disk sayısı.</summary>
    public const int MaxDisks = 64;
}

/// <summary>Agent canlılık bildirimi + temel envanter.</summary>
public sealed class AgentHeartbeatRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string? Fqdn { get; set; }
    public string? Domain { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? IpAddress { get; set; }
    public long? UptimeSeconds { get; set; }
    public DateTime SentAtUtc { get; set; }
}

public sealed class AgentHeartbeatResponse
{
    public DateTime ServerTimeUtc { get; set; }

    /// <summary>Sunucunun önerdiği metrik gönderim aralığı (saniye).</summary>
    public int RecommendedIntervalSeconds { get; set; }
}

/// <summary>Bir ölçüm anı. Yüzdeler 0-100 aralığındadır.</summary>
public sealed class AgentMetricsRequest
{
    public DateTime CollectedAtUtc { get; set; }
    public CpuMetric? Cpu { get; set; }
    public MemoryMetric? Memory { get; set; }
    public List<DiskMetric> Disks { get; set; } = new();
}

public sealed class CpuMetric
{
    public double UsagePercent { get; set; }
}

public sealed class MemoryMetric
{
    public double UsagePercent { get; set; }
    public long? TotalBytes { get; set; }
    public long? AvailableBytes { get; set; }
}

public sealed class DiskMetric
{
    /// <summary>Ör. "C:".</summary>
    public string Name { get; set; } = string.Empty;
    public double UsagePercent { get; set; }
    public long? TotalBytes { get; set; }
    public long? FreeBytes { get; set; }
}

/// <summary>
/// Agent'a gönderilen runbook işi. <see cref="Package"/> imzalı paketin kendisidir; agent imzayı kendi güvendiği
/// anahtarlarla ve runbook kimliğini kendi yerel izin listesiyle doğrular (sunucuya körü körüne güvenmez).
/// </summary>
public sealed class AgentCommand
{
    public Guid RunId { get; set; }
    public string Package { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new();
    public List<string> Switches { get; set; } = new();
    public bool DryRun { get; set; }
    public int TimeoutSeconds { get; set; }
}

public sealed class AgentCommandResult
{
    public bool DryRun { get; set; }
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
    public bool TimedOut { get; set; }

    /// <summary>Agent işi yerel kurallar nedeniyle reddetti (izin listesi, imza, parametre). Betik çalışmadı.</summary>
    public bool RejectedByAgent { get; set; }
}

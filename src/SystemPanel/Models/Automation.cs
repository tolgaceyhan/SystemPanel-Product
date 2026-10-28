using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.Models
{
    /// <summary>
    /// Runbook paketlerini imzalayabilecek güvenilen açık anahtar (ADR-002 §2). Ürünle gelen üretici anahtarı
    /// derlemeye gömülüdür ve bu tabloda değildir; bu tablo kurumun kendi anahtarlarını tutar.
    /// </summary>
    public class RunbookSigningKey
    {
        public Guid Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>SHA-256(SubjectPublicKeyInfo), küçük harf hex.</summary>
        [Required, MaxLength(64)]
        public string Fingerprint { get; set; } = string.Empty;

        [Required, MaxLength(1024)]
        public string PublicKeyPem { get; set; } = string.Empty;

        public bool IsRevoked { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? RevokedAtUtc { get; set; }

        [MaxLength(256)]
        public string? RevokedBy { get; set; }
    }

    /// <summary>
    /// İçe aktarılmış (imzası doğrulanmış) runbook sürümü. İçerik DEĞİŞMEZ; yeni sürüm yeni kayıttır.
    /// Çalıştırma anında <see cref="PackageText"/> yeniden doğrulanır.
    /// </summary>
    public class RunbookVersion
    {
        public Guid Id { get; set; }

        [Required, MaxLength(100)]
        public string RunbookId { get; set; } = string.Empty;

        [Required, MaxLength(32)]
        public string Version { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required, MaxLength(10)]
        public string Risk { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string TargetType { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Executor { get; set; } = string.Empty;

        public bool SupportsDryRun { get; set; }

        [Required, MaxLength(64)]
        public string ScriptSha256 { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string KeyFingerprint { get; set; } = string.Empty;

        /// <summary>İmzalı paketin kendisi (.sprb içeriği).</summary>
        [Required]
        public string PackageText { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public DateTime ImportedAtUtc { get; set; }

        [MaxLength(256)]
        public string? ImportedBy { get; set; }
    }

    public static class RunbookRunStatus
    {
        public const string PendingApproval = "PendingApproval";
        public const string Approved = "Approved";
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
        public const string TimedOut = "TimedOut";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
        public const string PreviewFailed = "PreviewFailed";

        // Agent çalıştırıcısı (ADR-002 Faz 2)
        /// <summary>Önizleme agent'ın almasını bekliyor.</summary>
        public const string PreviewPending = "PreviewPending";
        /// <summary>Önizleme agent'ta çalışıyor.</summary>
        public const string PreviewRunning = "PreviewRunning";
        /// <summary>İş agent'a teslim edildi, sonuç bekleniyor.</summary>
        public const string Dispatched = "Dispatched";

        public static readonly IReadOnlyCollection<string> Terminal = new[] { Succeeded, Failed, TimedOut, Rejected, Cancelled, Expired, PreviewFailed };
    }

    /// <summary>Bir runbook çalıştırma talebi ve sonucu.</summary>
    public class RunbookRun
    {
        public Guid Id { get; set; }

        public Guid RunbookVersionId { get; set; }
        public RunbookVersion? RunbookVersion { get; set; }

        [Required, MaxLength(30)]
        public string Status { get; set; } = RunbookRunStatus.PendingApproval;

        /// <summary>Doğrulanmış parametreler (JSON). Parola/anahtar türünde parametre desteklenmez.</summary>
        [Required]
        public string ParametersJson { get; set; } = "{}";

        [MaxLength(512)]
        public string? TargetSummary { get; set; }

        [MaxLength(500)]
        public string? Justification { get; set; }

        [Required, MaxLength(256)]
        public string RequestedBy { get; set; } = string.Empty;

        public DateTime RequestedAtUtc { get; set; }

        public int RequiredApprovals { get; set; }

        public DateTime? ApprovalExpiresAtUtc { get; set; }

        /// <summary>Önizleme (dry-run) çıktısı.</summary>
        public string? PreviewOutput { get; set; }

        public DateTime? StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public int? ExitCode { get; set; }

        /// <summary>Çalıştırma çıktısı (maskelenmiş, kısaltılmış).</summary>
        public string? Output { get; set; }

        [MaxLength(2000)]
        public string? Error { get; set; }

        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        /// <summary>Zamanlama/alarm tetiklemesiyle oluştuysa kaynağı (ADR-002 §5).</summary>
        public Guid? ScheduleId { get; set; }

        /// <summary>Agent çalıştırıcılı runbook'larda hedef agent.</summary>
        public Guid? TargetAgentId { get; set; }

        /// <summary>İşin (önizleme veya çalıştırma) agent'a teslim edildiği an.</summary>
        public DateTime? DispatchedAtUtc { get; set; }

        /// <summary>Teslim edilen iş için sonuç son tarihi (zaman aşımı + pay). Geçerse iş zaman aşımına düşer.</summary>
        public DateTime? DeadlineUtc { get; set; }

        public List<RunbookApproval> Approvals { get; set; } = new();
    }

    public static class RunbookScheduleStatus
    {
        /// <summary>Orta/yüksek riskli zamanlama ikinci bir yetkilinin onayını bekliyor (dört göz).</summary>
        public const string PendingApproval = "PendingApproval";
        public const string Active = "Active";
        public const string Rejected = "Rejected";
        public const string Disabled = "Disabled";
    }

    public static class RunbookTriggerTypes
    {
        public const string Cron = "cron";
        public const string Alarm = "alarm";
    }

    /// <summary>
    /// Zamanlanmış veya alarm tetiklemeli runbook (ADR-002 §5). Parametre kümesi ve runbook sürümü
    /// oluşturulurken sabitlenir; değiştirmek için yeni zamanlama oluşturulur.
    /// </summary>
    public class RunbookSchedule
    {
        public Guid Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public Guid RunbookVersionId { get; set; }
        public RunbookVersion? RunbookVersion { get; set; }

        public Guid? TargetAgentId { get; set; }

        [Required]
        public string ParametersJson { get; set; } = "{}";

        [MaxLength(512)]
        public string? TargetSummary { get; set; }

        [Required, MaxLength(10)]
        public string TriggerType { get; set; } = RunbookTriggerTypes.Cron;

        [MaxLength(100)]
        public string? CronExpression { get; set; }

        /// <summary>Alarm tetikleyici: "cpu" | "ram" | "disk".</summary>
        [MaxLength(10)]
        public string? AlarmMetric { get; set; }

        /// <summary>Disk alarmı için sürücü (ör. "C:"); boşsa tüm diskler.</summary>
        [MaxLength(8)]
        public string? AlarmDrive { get; set; }

        /// <summary>İki tetikleme arasındaki en kısa süre.</summary>
        public int MinIntervalMinutes { get; set; } = 60;

        [Required, MaxLength(20)]
        public string Status { get; set; } = RunbookScheduleStatus.PendingApproval;

        [MaxLength(500)]
        public string? Justification { get; set; }

        [Required, MaxLength(256)]
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }

        [MaxLength(256)]
        public string? DecidedBy { get; set; }
        public DateTime? DecidedAtUtc { get; set; }

        [MaxLength(256)]
        public string? DisabledBy { get; set; }
        public DateTime? DisabledAtUtc { get; set; }

        public DateTime? NextFireAtUtc { get; set; }
        public DateTime? LastFiredAtUtc { get; set; }
        public Guid? LastRunId { get; set; }

        [MaxLength(500)]
        public string? LastMessage { get; set; }
    }

    public class RunbookApproval
    {
        public Guid Id { get; set; }

        public Guid RunId { get; set; }
        public RunbookRun? Run { get; set; }

        [Required, MaxLength(256)]
        public string Approver { get; set; } = string.Empty;

        /// <summary>"approved" | "rejected"</summary>
        [Required, MaxLength(10)]
        public string Decision { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Comment { get; set; }

        public DateTime DecidedAtUtc { get; set; }
    }
}

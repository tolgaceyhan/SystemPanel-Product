using System;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.Models
{
    /// <summary>
    /// Güvenlik / yönetim işlemi denetim kaydı. Teknik loglardan (Serilog) AYRIDIR.
    /// Parola, LAPS parolası, token veya anahtar ASLA yazılmaz (bkz. Auditing/AuditRedactor).
    /// </summary>
    public class AuditEvent
    {
        public long Id { get; set; }

        public DateTime TimestampUtc { get; set; }

        [MaxLength(450)]
        public string? ActorUserId { get; set; }

        [MaxLength(256)]
        public string? ActorUserName { get; set; }

        /// <summary>User, Agent, System, Anonymous.</summary>
        [Required, MaxLength(32)]
        public string ActorType { get; set; } = "User";

        [MaxLength(64)]
        public string? SourceIp { get; set; }

        [Required, MaxLength(128)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? ResourceType { get; set; }

        [MaxLength(450)]
        public string? ResourceId { get; set; }

        [MaxLength(256)]
        public string? ResourceName { get; set; }

        /// <summary>İşlemi yapanın kapsamı (ör. "*" veya OU DN'leri).</summary>
        [MaxLength(1024)]
        public string? Scope { get; set; }

        public bool Success { get; set; }

        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        [MaxLength(64)]
        public string? ErrorCode { get; set; }

        [MaxLength(1024)]
        public string? ErrorMessage { get; set; }
    }
}

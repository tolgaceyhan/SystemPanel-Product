using System;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.Models
{
    /// <summary>
    /// Kayıtlı SystemPanel Agent'ı (sunucu başına bir kayıt). Gizli anahtarın kendisi SAKLANMAZ;
    /// yalnızca SHA-256 özeti ve tanımlama için ilk karakterleri tutulur.
    /// </summary>
    public class Agent
    {
        public Guid Id { get; set; }

        /// <summary>Agent'ın veri gönderebileceği tek sunucu adı (NetBIOS, büyük harf).</summary>
        [Required, MaxLength(100)]
        public string Hostname { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Description { get; set; }

        /// <summary>
        /// Sunucunun bağlı olduğu il/birim. Yönetici tarafından atanır; agent'ın bildirdiği değere
        /// güvenilmez. Ana sayfa metriklerinin il bazlı filtrelenmesinde kullanılır.
        /// </summary>
        [MaxLength(100)]
        public string? Province { get; set; }

        /// <summary>SHA-256(anahtar), hex.</summary>
        [Required, MaxLength(64)]
        public string KeyHash { get; set; } = string.Empty;

        /// <summary>Anahtarın ilk karakterleri (destek/tanımlama için; sır değildir).</summary>
        [Required, MaxLength(16)]
        public string KeyPrefix { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? KeyRotatedAtUtc { get; set; }

        public DateTime? LastSeenUtc { get; set; }

        [MaxLength(64)]
        public string? LastIpAddress { get; set; }

        [MaxLength(32)]
        public string? AgentVersion { get; set; }

        [MaxLength(128)]
        public string? OsName { get; set; }

        [MaxLength(64)]
        public string? OsVersion { get; set; }

        [MaxLength(256)]
        public string? Fqdn { get; set; }
    }
}

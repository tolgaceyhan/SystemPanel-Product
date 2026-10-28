using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemPanel.Models
{
    [Table("ServerInfos")] // veya veritabanındaki gerçek tablo adı

    public class ServerInfo
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(250)]
        public string Description { get; set; }

        [MaxLength(50)]
        public string IpAddress { get; set; }

        [MaxLength(50)]
        public string Type { get; set; }  // Physical / Virtual gibi

        [MaxLength(100)]
        public string? Location { get; set; } // 🔥 NULL OLABİLİR

        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public DateTime? LastSeenAt { get; set; }







    }
}
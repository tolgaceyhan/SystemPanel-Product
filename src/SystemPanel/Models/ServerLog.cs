using System;

namespace SystemPanel.Models
{
    public class ServerLog
    {
        public int Id { get; set; }
        public string Action { get; set; } = null!;
        public string ServerName { get; set; } = null!;
        public string PerformedBy { get; set; } = null!;
        public DateTime Timestamp { get; set; }
        public string? Details { get; set; }
    }
}

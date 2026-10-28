using System;

namespace SystemPanel.Models
{
    public class AdLogEntry
    {
        public int Id { get; set; }
        public string ActionBy { get; set; }
        public string ActionType { get; set; }
        public string TargetUser { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

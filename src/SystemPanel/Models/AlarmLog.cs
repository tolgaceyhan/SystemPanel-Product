using System;

namespace SystemPanel.Models
{
    public class AlarmLog
    {
        public int Id { get; set; }

        public string ServerName { get; set; } = "";
        public string AlarmType { get; set; } = "";
        public string Message { get; set; } = "";

        public DateTime CreatedAt { get; set; }  // Timestamp yerine bu
    }
}

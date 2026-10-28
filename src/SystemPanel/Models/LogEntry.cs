namespace SystemPanel.Models
{
    public class LogEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string User { get; set; }
        public string IP { get; set; }
    }
}



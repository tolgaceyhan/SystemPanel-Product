namespace SystemPanel.Models
{
    public class AutoHealLog
    {
        public int Id { get; set; }
        public string Province { get; set; } = "";
        public string Action { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}

namespace SystemPanel.Models
{
    public class AutoHealSettings
    {
        public int Id { get; set; }
        public int CooldownMinutes { get; set; } = 10;
        public bool Enabled { get; set; } = true;
    }
}

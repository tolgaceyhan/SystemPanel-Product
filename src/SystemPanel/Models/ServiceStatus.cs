namespace SystemPanel.Models
{
    public class ServiceStatus
    {
        public int Id { get; set; }                        // int olsun
        public string ServiceName { get; set; } = "";
        public string State { get; set; } = "";            // Running | Stopped | Unreachable ...
        public string RawStatus { get; set; } = "";        // OK | Error | WMI Status
        public string? Note { get; set; }                  // Örn: (CRITICAL DOWN)

        // View’ların kullandığı tek alan: hesaplanır, set EDİLMEZ
        public string Status => string.IsNullOrWhiteSpace(Note)
            ? (RawStatus ?? string.Empty)
            : $"{(RawStatus ?? string.Empty)} {Note}".Trim();
    }
}

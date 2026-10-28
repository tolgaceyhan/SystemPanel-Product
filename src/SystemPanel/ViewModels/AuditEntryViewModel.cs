using System;

namespace SystemPanel.ViewModels
{
    public class AuditEntryViewModel
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; } = "";
        public string Action { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Ip { get; set; } = "";
    }
}

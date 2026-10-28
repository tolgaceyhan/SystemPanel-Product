using System;

namespace SystemPanel.ViewModels.Gpo
{
    public class GpoLogViewModel
    {
        public DateTime Timestamp { get; set; }
        public string Username { get; set; }
        public string Action { get; set; }
        public string Description { get; set; }
    }
}

using SystemPanel.Models;

namespace SystemPanel.ViewModels.Server
{
    public class ServerFilterViewModel
    {
        public string? SearchTerm { get; set; }
        public string? Type { get; set; }
        public bool? IsActive { get; set; }

        public List<ServerInfo> Servers { get; set; } = new();
    }
}

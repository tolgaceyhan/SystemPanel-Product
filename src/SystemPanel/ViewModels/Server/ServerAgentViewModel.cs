namespace SystemPanel.ViewModels.Server
{
    public class ServerAgentViewModel
    {
        public string ServerName { get; set; }
        public bool AgentInstalled { get; set; }
        public bool AgentOnline { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }
}
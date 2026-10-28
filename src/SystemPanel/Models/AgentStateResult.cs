
namespace SystemPanel.Models
{
    public class AgentStateResult
    {
        public bool AgentInstalled { get; set; }
        public bool AgentOnline { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }
}
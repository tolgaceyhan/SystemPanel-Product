public class DiscoveredServerViewModel
{
    public string Name { get; set; }
    public string IpAddress { get; set; }
    public string Ou { get; set; }

    // Agent bilgisi
    public bool AgentInstalled { get; set; }
    public bool AgentOnline { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

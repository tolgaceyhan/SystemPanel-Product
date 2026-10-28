namespace SystemPanel.Models
{
    public class AdServer
    {
        internal DateTime? LastSeenAt;

        public string Name { get; set; }

        // 👇 DNS hostname (SRV01.domain.local gibi)
        public string? DnsHostName { get; set; }

        public string? OperatingSystem { get; set; }
        public string? DistinguishedName { get; set; }
        public string? OrganizationalUnit { get; set; }
        public string? Description { get; set; }

        public string? IpAddress { get; set; }
        public bool AgentInstalled { get; set; }
        public bool AgentOnline { get; set; }

        public DateTime? LastLogonDate { get; set; }
    }
}

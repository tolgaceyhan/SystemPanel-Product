namespace SystemPanel.Models
{
    public class SccmDpServerInfo
    {
        public string ServerName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string SiteCode { get; set; } = "";

        public List<ServiceStatus> Services { get; set; } = new();
    }

}
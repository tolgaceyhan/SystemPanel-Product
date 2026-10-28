namespace SystemPanel.Models
{
    public class SccmDpSnapshot
    {
        public string ServerName { get; set; }
        public string SiteCode { get; set; }
        public string IpAddress { get; set; }

        public int TotalServices { get; set; }
        public int RunningServices { get; set; }
        public int HealthScore { get; set; }

        public int CriticalDownCount { get; set; }

        public DateTime LastUpdated { get; set; }

        public string BoundaryGroupName { get; set; } = "";


    }

}

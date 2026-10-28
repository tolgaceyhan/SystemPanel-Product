namespace SystemPanel.Models
{
    public class DpServerInfo
    {
        public string ServerName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string ProvinceName { get; set; } = "";

        public bool WdsRunning { get; set; }
        public bool CcmExecRunning { get; set; }

        public string? AdditionalInfo { get; set; }
    }

}
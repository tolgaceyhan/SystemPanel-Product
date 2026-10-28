namespace SystemPanel.Models
{
    public class ServerDiskHistory
    {
        public int Id { get; set; }

        public string ServerName { get; set; } = null!;
        public string DriveName { get; set; } = null!;

        public int Usage { get; set; }   // yüzde

        public DateTime CreatedAt { get; set; }
    }
}

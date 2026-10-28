namespace SystemPanel.ViewModels.AdComputer
{
    public class AdComputerViewModel
    {
        public string Name { get; set; } = "";
        public string DistinguishedName { get; set; } = "";
        public string? OperatingSystem { get; set; }
        public string? Description { get; set; }
        public System.DateTime? LastLogonDate { get; set; }
        public string Status { get; set; } = "Aktif"; // "Aktif" / "Pasif"

    }
}

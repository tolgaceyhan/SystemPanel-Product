namespace SystemPanel.ViewModels.Server
{
    public class EditServerViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IpAddress { get; set; }
        public string? Type { get; set; } // Physical / Virtual
        public string? Location { get; set; }
        public bool IsActive { get; set; }
    }
}

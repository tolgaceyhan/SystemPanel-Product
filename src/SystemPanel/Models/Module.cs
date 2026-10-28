namespace SystemPanel.Models
{
    public class Module
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Route { get; set; }
        public int? Order { get; set; }
        public string Icon { get; set; }
        public string Category { get; set; }
    }
}

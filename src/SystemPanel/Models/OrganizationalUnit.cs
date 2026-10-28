namespace SystemPanel.Models
{
    public class OrganizationalUnit
    {
        public string DistinguishedName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

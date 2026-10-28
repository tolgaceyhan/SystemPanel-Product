
namespace SystemPanel.Models
{
    public class OuTreeDto
    {
        public string OuName { get; set; } // Added this property to match the usage in the code
        public string DistinguishedName { get; set; }
        public List<OuTreeDto> Children { get; set; } = new List<OuTreeDto>();
    }
}

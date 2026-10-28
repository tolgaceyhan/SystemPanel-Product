using System;
using SystemPanel.Configuration;
   
namespace SystemPanel.Models
{

    // Ensure this is the only definition of OuNodeVm in the namespace
    public class OuNodeVm
    {
        public string Id { get; set; } = string.Empty;           // DN or GUID
        public string Label { get; set; } = string.Empty;        // Clean display name
        public string DistinguishedName { get; set; } = string.Empty;
        public string Type { get; set; } = "OU";                 // OU, CN, etc.
        public int ChildCount { get; set; }
    }
}
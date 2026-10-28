using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using SystemPanel.Models;

namespace SystemPanel.ViewModels.AdUser
{
    public class OuSearchViewModel
    {
        public string SearchTerm { get; set; } // opsiyonel serbest arama
        public string SelectedOu { get; set; }  // dropdown seçimi
        public List<OrganizationalUnit> Results { get; set; } = new();
        public List<SelectListItem> AvailableOus { get; set; } = new(); // dropdown için
        public List<string> SelectedOus { get; set; } = new();
        public string SelectedGpo { get; set; }
        public List<SelectListItem> AvailableGpos { get; set; } = new();
        public string? Term { get; set; }
        public string? Ou { get; set; }
        public IEnumerable<string> RootDns { get; set; } = Enumerable.Empty<string>();


    }
}



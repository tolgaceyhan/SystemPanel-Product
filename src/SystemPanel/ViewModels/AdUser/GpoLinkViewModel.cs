using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SystemPanel.ViewModels.Gpo
{
    public class GpoLinkViewModel
    {
        public string OuPath { get; set; }
        public string SelectedGpo { get; set; }
        public List<SelectListItem> AvailableGpos { get; set; } = new();
    }
}

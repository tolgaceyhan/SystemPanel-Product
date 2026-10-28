
using System.Collections.Generic;

namespace SystemPanel.ViewModels
{
    public class ServerSelectionViewModel
    {
        public string SelectionMode { get; set; } // "All" or "Ou"
        public string OuPath { get; set; }
        public List<string> ServerList { get; set; }
    }
}

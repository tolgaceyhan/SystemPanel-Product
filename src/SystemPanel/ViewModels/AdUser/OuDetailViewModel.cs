using System.Collections.Generic;

namespace SystemPanel.ViewModels.AdUser
{
    public class OuDetailViewModel
    {
        public string OuPath { get; set; }
        public string DistinguishedName { get; set; }

        public IEnumerable<SystemPanel.Models.AdUser> Users { get; set; }
        public IEnumerable<SystemPanel.Models.AdComputer> Computers { get; set; }

        public List<string> LinkedGpos { get; set; }
    }
}

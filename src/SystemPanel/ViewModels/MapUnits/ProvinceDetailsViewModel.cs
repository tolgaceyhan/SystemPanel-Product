using System.Collections.Generic;
using SystemPanel.Models;

namespace SystemPanel.ViewModels
{
    public class ProvinceDetailsViewModel
    {
        public string ProvinceName { get; set; } = "";
        public List<DpServerInfo> DistributionServers { get; set; }
            = new();
    }
}

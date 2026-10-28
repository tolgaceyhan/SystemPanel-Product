using System.Collections.Generic;
using SystemPanel.Models;

namespace SystemPanel.ViewModels.Sccm
{
    public class SccmDpDetailsViewModel
    {
        public SccmDpSnapshot Snapshot { get; set; }
        public SccmDpServerInfo Live { get; set; }
    }
}


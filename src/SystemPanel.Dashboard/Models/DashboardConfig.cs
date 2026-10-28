using System.Collections.Generic;

namespace SystemPanel.Dashboard.Models
{
    public class DashboardConfig
    {
        public int LayoutType { get; set; }
        public List<DashboardPanel> Panels { get; set; } = new();
    }

    public class DashboardPanel
    {
        public int Id { get; set; }                 // ✅ ekledik
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public int Position { get; set; }
        public int? RefreshIntervalSec { get; set; }
        public double? ZoomFactor { get; set; }

    }

    public class DashboardOrderDto
    {
        public int Id { get; set; }
        public int Position { get; set; }
    }
}

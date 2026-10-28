using System.Collections.Generic;

namespace SystemPanel.ViewModels.AdComputer
{
    public class AdComputerListViewModel
    {
        public IEnumerable<AdComputerViewModel> Items { get; set; } = new List<AdComputerViewModel>();
        public string? SearchTerm { get; set; }
        public string? Ou { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int Total { get; set; }
        public int TotalPages => PageSize > 0 ? (int)System.Math.Ceiling((double)Total / PageSize) : 1;
    }
}

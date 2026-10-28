using System.Collections.Generic;
using System.Linq;

namespace SystemPanel.ViewModels.Gpo
{
    public class GpoOuTreeViewModel
    {
        public string OuName { get; set; }                    // Görünen isim
        public string DistinguishedName { get; set; }         // OU'nun gerçek DN'i
        public List<string> LinkedGpos { get; set; } = new(); // OU’ya bağlı GPO isimleri
        public List<GpoOuTreeViewModel> ChildOus { get; set; } = new(); // Alt OU’lar

        public bool HasChildren => ChildOus?.Any() == true;
    }
}

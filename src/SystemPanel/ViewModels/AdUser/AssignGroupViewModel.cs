using System.Collections.Generic;
using SystemPanel.Models; // AdUser sınıfının bulunduğu namespace

namespace SystemPanel.ViewModels.AdUser
{
    public class AssignGroupViewModel
    {
        public string OuPath { get; set; }

        // OU altındaki kullanıcılar (listeleme için)
        public List<SystemPanel.Models.AdUser> Users { get; set; } = new List<SystemPanel.Models.AdUser>();

        // Formdan gelen seçili kullanıcılar (checkbox)
        public List<string> SelectedUsers { get; set; } = new List<string>();

        // Atanacak grup adı
        public string GroupName { get; set; }
    }
}

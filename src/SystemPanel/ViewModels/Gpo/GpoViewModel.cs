using System;
using System.Collections.Generic;

namespace SystemPanel.ViewModels.Gpo
{
    public class GpoViewModel
    {
        public string Id { get; set; }                  // GPO GUID
        public string DisplayName { get; set; }         // GPO adı
        public string Description { get; set; }         // Açıklama

        /// <summary>
        /// Microsoft.GroupPolicy.GpoStatus adı: AllSettingsEnabled, AllSettingsDisabled,
        /// UserSettingsDisabled, ComputerSettingsDisabled (betik sayısal değer değil ad döndürür).
        /// </summary>
        public string GpoStatus { get; set; } = string.Empty;

        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }

        public string Owner { get; set; }
        public string DomainName { get; set; }
        public string GpoVersion { get; set; }
        public string GpoPath { get; set; }
        public string GpoType { get; set; }             // e.g. "Computer", "User", "All"

        public List<string> LinkedOus { get; set; } = new();   // OU link listesi
        public List<string> LinkedSites { get; set; } = new(); // Site link listesi

        public bool IsEnabled => string.Equals(GpoStatus, "AllSettingsEnabled", StringComparison.OrdinalIgnoreCase);

        public bool IsFullyDisabled => string.Equals(GpoStatus, "AllSettingsDisabled", StringComparison.OrdinalIgnoreCase);
    }
}

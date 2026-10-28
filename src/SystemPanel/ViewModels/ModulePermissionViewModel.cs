using System;
using System.Collections.Generic;

namespace SystemPanel.ViewModels
{
    public class RolePermissionMatrixViewModel
    {
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();

        /// <summary>"Rol|izin" anahtarları.</summary>
        public HashSet<string> Granted { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsGranted(string role, string permission) => Granted.Contains($"{role}|{permission}");
    }
}

using System.ComponentModel.DataAnnotations;

namespace SystemPanel.Models
{
    /// <summary>Rol → izin eşlemesi (bkz. Security/Permissions.cs).</summary>
    public class RolePermission
    {
        public int Id { get; set; }

        [Required, MaxLength(256)]
        public string RoleName { get; set; } = string.Empty;

        [Required, MaxLength(128)]
        public string Permission { get; set; } = string.Empty;
    }
}

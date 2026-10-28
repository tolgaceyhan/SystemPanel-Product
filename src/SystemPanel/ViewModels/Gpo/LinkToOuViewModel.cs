using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.Gpo
{
    public class LinkToOuViewModel
    {
        [Required]
        public string GpoId { get; set; } = string.Empty;

        /// <summary>Yalnızca görüntüleme / audit için.</summary>
        public string? GpoName { get; set; }

        /// <summary>Hedef OU'nun distinguishedName değeri.</summary>
        [Required(ErrorMessage = "Hedef OU seçilmelidir.")]
        public string OrganizationalUnit { get; set; } = string.Empty;
    }
}

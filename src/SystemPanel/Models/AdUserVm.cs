
namespace SystemPanel.Models
{
    public class AdUserVm
    {
        public string DistinguishedName { get; init; } = string.Empty;
        public string SamAccountName { get; init; } = string.Empty;
        public string UserPrincipalName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Mail { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }
}

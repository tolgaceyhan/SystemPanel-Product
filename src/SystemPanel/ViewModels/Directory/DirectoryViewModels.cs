namespace SystemPanel.ViewModels.Directory
{
    public class OuNodeVm
    {
        public string Id { get; init; } = string.Empty;            // DN or GUID
        public string Label { get; init; } = string.Empty;         // Clean display name
        public string DistinguishedName { get; init; } = string.Empty;
        public string Type { get; init; } = "OU";                  // OU, Container, ...
        public int ChildCount { get; init; }
    }

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

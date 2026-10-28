namespace SystemPanel.ViewModels.AdUser
{
    public class LocalAdminScopeViewModel
    {
        public string RequestedOu { get; set; }
        public bool IsAuthorized { get; set; }
        public List<string> AllowedOuDns { get; set; }
        public string Message { get; set; }
    }
}

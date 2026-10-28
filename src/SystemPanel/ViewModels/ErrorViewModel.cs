namespace SystemPanel.ViewModels
{
    public class ErrorViewModel
    {
        /// <summary>Correlation ID: kullanıcı bunu destek ekibine iletir; loglarda aynı ID aranır.</summary>
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public string? Message { get; set; }

        /// <summary>Kullanıcıya gösterilmez (teknik ayrıntı yalnızca logda).</summary>
        public string? Detail { get; set; }
    }
}

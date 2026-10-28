namespace SystemPanel.Services
{
 

    public class SmsSelectionRequest
    {
        public List<string> DistinguishedNames { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }
}
namespace SystemPanel.Services
{
    public interface IMailService
    {
        Task SendAlertAsync(string subject, string message);
    }

}

namespace SystemPanel.Services
{
    public interface INotificationService
    {
        void Send(string subject, string message);
    }

}

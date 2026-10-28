using Microsoft.AspNetCore.SignalR;

public class ServiceStatusHub : Hub
{
    public async Task UpdateServiceStatus(string serviceName, string status)
    {
        await Clients.All.SendAsync("ReceiveServiceStatus", serviceName, status);
    }
}

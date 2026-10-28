using Microsoft.AspNetCore.SignalR;

namespace SystemPanel.Hubs
{
    public class MonitoringHub : Hub
    {
        // Anlık olarak sunucu metriklerini gönderir
        public async Task SendMetricsUpdate(string metrics)
        {
            await Clients.All.SendAsync("ReceiveMetrics", metrics); // Anlık veri güncellenmesi yapılır
        }
    }
}

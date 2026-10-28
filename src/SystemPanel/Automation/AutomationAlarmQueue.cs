using System;
using System.Threading.Channels;

namespace SystemPanel.Automation
{
    /// <param name="Hostname">Alarmı üreten sunucu (agent kayıt adı).</param>
    /// <param name="Metric">"cpu" | "ram" | "disk"</param>
    /// <param name="Drive">Disk alarmında sürücü (ör. "C:").</param>
    public sealed record AutomationAlarm(string Hostname, string Metric, string? Drive, int Value, DateTime AtUtc);

    /// <summary>
    /// Metrik alımından otomasyona alarm iletimi. Sınırlı ve kayıplıdır (doluysa en eskiler atılır):
    /// alım hattı otomasyon yüzünden asla yavaşlamaz veya hata vermez.
    /// </summary>
    public sealed class AutomationAlarmQueue
    {
        private readonly Channel<AutomationAlarm> _channel = Channel.CreateBounded<AutomationAlarm>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        public void Publish(AutomationAlarm alarm) => _channel.Writer.TryWrite(alarm);

        public bool TryRead(out AutomationAlarm? alarm)
        {
            var ok = _channel.Reader.TryRead(out var item);
            alarm = item;
            return ok;
        }
    }
}

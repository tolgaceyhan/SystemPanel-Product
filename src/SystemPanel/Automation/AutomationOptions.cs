using System;
using System.IO;

namespace SystemPanel.Automation
{
    public sealed class AutomationOptions
    {
        public const string SectionName = "Automation";

        /// <summary>Açılışta içe aktarılacak imzalı paketlerin (*.sprb) klasörü. Boşsa ContentRoot/runbooks.</summary>
        public string? CatalogDirectory { get; set; }

        /// <summary>Onay bekleyen taleplerin geçerlilik süresi.</summary>
        public int ApprovalExpiryHours { get; set; } = 24;

        /// <summary>Windows PowerShell 5.1 (ActiveDirectory modülü için).</summary>
        public string PowerShellPath { get; set; } = "powershell.exe";

        /// <summary>Betiklerin geçici olarak yazıldığı klasör. Boşsa %ProgramData%\SystemPanel\Automation\work.</summary>
        public string? WorkDirectory { get; set; }

        /// <summary>Kaydedilen çıktı üst sınırı (bayt).</summary>
        public int MaxOutputBytes { get; set; } = 64 * 1024;

        /// <summary>Onaylanmış işlerin kuyruktan alınma aralığı.</summary>
        public int PollSeconds { get; set; } = 5;

        /// <summary>Cron ifadelerinin değerlendirildiği saat dilimi (Windows veya IANA kimliği). Boşsa sunucunun yerel saati.</summary>
        public string? ScheduleTimeZone { get; set; }

        /// <summary>Zamanlamalar için izin verilen en kısa tetikleme aralığı (dakika).</summary>
        public int MinScheduleIntervalMinutes { get; set; } = 5;

        public TimeZoneInfo ResolveTimeZone()
        {
            if (string.IsNullOrWhiteSpace(ScheduleTimeZone))
                return TimeZoneInfo.Local;
            try { return TimeZoneInfo.FindSystemTimeZoneById(ScheduleTimeZone); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Local; }
        }

        public string ResolveWorkDirectory()
            => string.IsNullOrWhiteSpace(WorkDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "Automation", "work")
                : WorkDirectory;
    }
}

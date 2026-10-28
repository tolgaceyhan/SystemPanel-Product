using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SystemPanel.Dashboard.Core
{
    /// <summary>
    /// Basit günlük dosyalı log (logs\dashboard-yyyyMMdd.log, 14 gün saklanır). Log yazılamazsa
    /// uygulama çalışmaya devam eder; kiosk ekranında hata kutusu açılmaz.
    /// </summary>
    public static class DashboardLog
    {
        private const int RetentionDays = 14;
        private static readonly object Sync = new();
        private static string? _directory;
        private static DateTime _lastCleanup = DateTime.MinValue;

        public static void Initialize(string directory) => _directory = directory;

        public static void Info(string message) => Write("INF", message);
        public static void Warn(string message) => Write("WRN", message);
        public static void Error(string message, Exception? ex = null)
            => Write("ERR", ex == null ? message : message + " " + ex);

        private static void Write(string level, string message)
        {
            var dir = _directory;
            if (dir == null)
                return;

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(dir);
                    var now = DateTime.Now;
                    var path = Path.Combine(dir, $"dashboard-{now:yyyyMMdd}.log");
                    File.AppendAllText(path, $"[{now:yyyy-MM-dd HH:mm:ss.fff} {level}] {message}{Environment.NewLine}", Encoding.UTF8);
                    CleanupIfDue(dir, now);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void CleanupIfDue(string dir, DateTime now)
        {
            if (now - _lastCleanup < TimeSpan.FromHours(6))
                return;
            _lastCleanup = now;

            foreach (var file in Directory.EnumerateFiles(dir, "dashboard-*.log")
                         .Where(f => File.GetLastWriteTime(f) < now.AddDays(-RetentionDays)))
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

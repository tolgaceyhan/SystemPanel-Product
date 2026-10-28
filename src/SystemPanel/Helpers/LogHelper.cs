
using System;
using System.IO;

namespace SystemPanel.Helpers
{
    public static class LogHelper
    {
        private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public static void LogServiceCheck(string serverName, string serviceName, string state, string status)
        {
            try
            {
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);

                string logFile = Path.Combine(logDirectory, "ServiceHistory.log");
                string logLine = $"{DateTime.Now:u} | {serverName} | {serviceName} | {state} | {status}";
                File.AppendAllText(logFile, logLine + Environment.NewLine);
            }
            catch
            {
                // Logging errors are silently ignored
            }
        }
    }
}

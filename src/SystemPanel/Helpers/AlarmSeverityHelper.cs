using SystemPanel.Models;
using System.Text.RegularExpressions;

namespace SystemPanel.Helpers
{
    public static class AlarmSeverityHelper
    {
        // Mesajdan yüzde değerini çek
        private static int ExtractPercentage(string message)
        {
            var match = Regex.Match(message, @"\b(\d+)%");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static string GetSeverityClass(AlarmLog alarm)
        {
            int value = ExtractPercentage(alarm.Message);

            // Kritik seviye
            if (value >= 95)
                return "table-danger";

            // Uyarı seviyesi
            if (value >= 85)
                return "table-warning";

            // Normal
            return "";
        }
    }
}

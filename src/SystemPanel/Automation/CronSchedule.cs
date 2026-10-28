using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SystemPanel.Automation
{
    /// <summary>
    /// 5 alanlı cron ifadesi: dakika saat gün ay haftanın-günü.
    /// Desteklenen: <c>*</c>, sayı, liste (<c>1,15</c>), aralık (<c>1-5</c>), adım (<c>*/15</c>, <c>0-30/10</c>),
    /// ay adları (JAN-DEC) ve gün adları (SUN-SAT; 0 ve 7 = Pazar).
    /// Gün ve haftanın günü ikisi de kısıtlıysa klasik cron gibi VEYA ile birleşir.
    /// Zamanlar verilen saat diliminde değerlendirilir (yaz saati geçişlerinde var olmayan saatler atlanır).
    /// </summary>
    public sealed class CronSchedule
    {
        private static readonly string[] MonthNames = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
        private static readonly string[] DayNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

        private readonly bool[] _minutes = new bool[60];
        private readonly bool[] _hours = new bool[24];
        private readonly bool[] _days = new bool[32];
        private readonly bool[] _months = new bool[13];
        private readonly bool[] _weekdays = new bool[7];
        private readonly bool _dayRestricted;
        private readonly bool _weekdayRestricted;

        public string Expression { get; }

        private CronSchedule(string expression, string[] f)
        {
            Expression = expression;
            Fill(_minutes, f[0], 0, 59, null);
            Fill(_hours, f[1], 0, 23, null);
            Fill(_days, f[2], 1, 31, null);
            Fill(_months, f[3], 1, 12, MonthNames);
            FillWeekdays(f[4]);
            _dayRestricted = f[2] != "*";
            _weekdayRestricted = f[4] != "*";
        }

        public static bool TryParse(string? expression, out CronSchedule? schedule, out string? error)
        {
            schedule = null;
            error = null;

            var fields = (expression ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5)
            {
                error = "Cron ifadesi 5 alandan oluşmalı: dakika saat gün ay haftanın-günü (ör. \"0 3 * * *\").";
                return false;
            }

            try
            {
                schedule = new CronSchedule(string.Join(' ', fields), fields.Select(f => f.ToUpperInvariant()).ToArray());
                if (!schedule._months.Any(m => m) || !schedule._minutes.Any(m => m) || !schedule._hours.Any(h => h))
                    throw new FormatException("boş küme");
                return true;
            }
            catch (FormatException ex)
            {
                schedule = null;
                error = "Cron ifadesi geçersiz: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// <paramref name="afterUtc"/> anından SONRAKİ ilk tetikleme (UTC). Dakika hassasiyeti.
        /// 4 yıl içinde eşleşme yoksa (ör. 30 Şubat) null.
        /// </summary>
        public DateTime? NextAfter(DateTime afterUtc, TimeZoneInfo zone)
        {
            var utc = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            var t = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
            var limit = t.AddYears(4);

            while (t < limit)
            {
                if (!_months[t.Month])
                {
                    t = new DateTime(t.Year, t.Month, 1).AddMonths(1);
                    continue;
                }
                if (!DayMatches(t))
                {
                    t = t.Date.AddDays(1);
                    continue;
                }
                if (!_hours[t.Hour])
                {
                    t = t.Date.AddHours(t.Hour + 1);
                    continue;
                }
                if (!_minutes[t.Minute])
                {
                    t = t.AddMinutes(1);
                    continue;
                }

                if (!zone.IsInvalidTime(t))
                    return TimeZoneInfo.ConvertTimeToUtc(t, zone);
                t = t.AddMinutes(1);
            }

            return null;
        }

        private bool DayMatches(DateTime t)
        {
            var dom = _days[t.Day];
            var dow = _weekdays[(int)t.DayOfWeek];
            if (_dayRestricted && _weekdayRestricted)
                return dom || dow;
            return dom && dow;
        }

        private void FillWeekdays(string field)
        {
            var tmp = new bool[8];
            Fill(tmp, field, 0, 7, DayNames);
            for (var i = 0; i < 7; i++)
                _weekdays[i] = tmp[i];
            if (tmp[7])
                _weekdays[0] = true;
        }

        private static void Fill(bool[] target, string field, int min, int max, string[]? names)
        {
            foreach (var part in field.Split(','))
            {
                if (part.Length == 0)
                    throw new FormatException($"'{field}' boş öğe içeriyor");

                var step = 1;
                var rangePart = part;
                var slash = part.IndexOf('/');
                if (slash >= 0)
                {
                    if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1 || step > max)
                        throw new FormatException($"'{part}' adımı geçersiz");
                    rangePart = part[..slash];
                }

                int from, to;
                if (rangePart == "*")
                {
                    from = min;
                    to = max;
                }
                else if (rangePart.Contains('-'))
                {
                    var bounds = rangePart.Split('-');
                    if (bounds.Length != 2)
                        throw new FormatException($"'{part}' aralığı geçersiz");
                    from = Value(bounds[0], min, max, names);
                    to = Value(bounds[1], min, max, names);
                    if (from > to)
                        throw new FormatException($"'{part}' aralığı ters");
                }
                else
                {
                    from = Value(rangePart, min, max, names);
                    to = slash >= 0 ? max : from;
                }

                for (var v = from; v <= to; v += step)
                    target[v] = true;
            }
        }

        private static int Value(string token, int min, int max, string[]? names)
        {
            if (names != null)
            {
                var index = Array.IndexOf(names, token);
                if (index >= 0)
                    return names == MonthNames ? index + 1 : index;
            }

            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw new FormatException($"'{token}' {min}-{max} aralığında olmalı");
            return value;
        }
    }
}

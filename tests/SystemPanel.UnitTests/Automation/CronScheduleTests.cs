using SystemPanel.Automation;

namespace SystemPanel.UnitTests.Automation;

public sealed class CronScheduleTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTime Next(string expr, string afterUtc, TimeZoneInfo? zone = null)
    {
        Assert.True(CronSchedule.TryParse(expr, out var cron, out var error), error);
        return cron!.NextAfter(DateTime.Parse(afterUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), zone ?? Utc)!.Value;
    }

    [Theory]
    [InlineData("0 3 * * *", "2026-10-28T10:00:00Z", "2026-10-29T03:00:00Z")]
    [InlineData("0 3 * * *", "2026-10-28T02:59:00Z", "2026-10-28T03:00:00Z")]
    [InlineData("*/15 * * * *", "2026-10-28T10:07:00Z", "2026-10-28T10:15:00Z")]
    [InlineData("30 8 * * MON-FRI", "2026-10-30T09:00:00Z", "2026-11-02T08:30:00Z")] // Cuma sonrası → Pazartesi
    [InlineData("0 0 1 JAN *", "2026-10-28T00:00:00Z", "2027-01-01T00:00:00Z")]
    [InlineData("0 12 * * 0", "2026-10-28T00:00:00Z", "2026-11-01T12:00:00Z")]  // Pazar
    [InlineData("0 12 * * 7", "2026-10-28T00:00:00Z", "2026-11-01T12:00:00Z")]  // 7 = Pazar
    [InlineData("0 6 1,15 * *", "2026-10-28T00:00:00Z", "2026-11-01T06:00:00Z")]
    [InlineData("0 0 29 2 *", "2026-10-28T00:00:00Z", "2028-02-29T00:00:00Z")]  // artık yıl
    public void NextOccurrence(string expr, string after, string expected)
        => Assert.Equal(DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), Next(expr, after));

    [Fact]
    public void DayOfMonthAndWeekday_AreOred()
        // 13'ü veya Cuma: 28 Ekim 2026 Çarşamba → ilk eşleşme 30 Ekim Cuma
        => Assert.Equal(new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), Next("0 0 13 * FRI", "2026-10-28T00:00:00Z"));

    [Fact]
    public void EvaluatedInConfiguredTimeZone()
    {
        var istanbul = TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "TRT", "TRT");
        // 03:00 İstanbul = 00:00 UTC
        Assert.Equal(new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), Next("0 3 * * *", "2026-10-28T10:00:00Z", istanbul));
    }

    [Fact]
    public void ImpossibleDate_ReturnsNull()
    {
        Assert.True(CronSchedule.TryParse("0 0 30 2 *", out var cron, out _));
        Assert.Null(cron!.NextAfter(DateTime.UtcNow, Utc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("a * * * *")]
    [InlineData("1,,2 * * * *")]
    public void Invalid_IsRejected(string expr)
        => Assert.False(CronSchedule.TryParse(expr, out _, out _));
}

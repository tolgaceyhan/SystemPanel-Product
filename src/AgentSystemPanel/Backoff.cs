namespace AgentSystemPanel;

/// <summary>Ardışık hatalarda üstel bekleme + jitter (tüm agent'ların aynı anda yüklenmesini önler).</summary>
public static class Backoff
{
    public static TimeSpan Compute(int consecutiveFailures, TimeSpan baseDelay, TimeSpan maxDelay, double jitter01)
    {
        if (consecutiveFailures <= 0)
            return baseDelay;

        var exp = Math.Min(consecutiveFailures, 16);
        var raw = baseDelay.TotalSeconds * Math.Pow(2, exp);
        var capped = Math.Min(raw, maxDelay.TotalSeconds);

        // ±%20 jitter
        var factor = 0.8 + 0.4 * Math.Clamp(jitter01, 0, 1);
        return TimeSpan.FromSeconds(Math.Max(baseDelay.TotalSeconds, capped * factor));
    }
}

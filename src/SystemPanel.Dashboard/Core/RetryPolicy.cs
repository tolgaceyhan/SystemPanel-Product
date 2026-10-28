using System;

namespace SystemPanel.Dashboard.Core
{
    /// <summary>Üstel geri çekilme + jitter: 5 sn, 10 sn, 20 sn ... en fazla 5 dk.</summary>
    public static class RetryPolicy
    {
        public static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

        public static TimeSpan Delay(int consecutiveFailures, Random? random = null)
        {
            if (consecutiveFailures <= 0)
                return Initial;

            var exponent = Math.Min(consecutiveFailures - 1, 10);
            var baseMs = Math.Min(Initial.TotalMilliseconds * Math.Pow(2, exponent), Max.TotalMilliseconds);
            var jitter = 0.8 + (random ?? Random.Shared).NextDouble() * 0.4;
            return TimeSpan.FromMilliseconds(Math.Min(baseMs * jitter, Max.TotalMilliseconds));
        }
    }
}

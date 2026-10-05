using TraceLensService.Enums;
using TraceLensService.Models.Internal;

namespace TraceLensService.Models.Requests
{
    /// <summary>
    /// Ortak query string filtreleri. Zaman aralığı ya <see cref="Range"/> (15m, 1h, 24h, 7d)
    /// ya da <see cref="From"/>/<see cref="To"/> (ISO-8601) ile verilir.
    /// </summary>
    public class TraceFilterRequest
    {
        public string? Range { get; set; }
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public string? Service { get; set; }
        public string? Operation { get; set; }
        public double? MinDurationMs { get; set; }
        public bool? OnlyErrors { get; set; }
        public bool? OnlySlow { get; set; }

        public TraceFilter ToFilter(AppKind app)
        {
            (DateTimeOffset from, DateTimeOffset to) = ResolveRange();
            return new TraceFilter(app, from, to, Service, Operation, MinDurationMs, OnlyErrors ?? false, OnlySlow ?? false);
        }

        /// <summary>Zaman aralığını çözer: from/to verilmişse onlar, yoksa şimdi - range.</summary>
        public (DateTimeOffset From, DateTimeOffset To) ResolveRange()
        {
            DateTimeOffset to = To ?? DateTimeOffset.UtcNow;
            return (From ?? to - ParseRange(Range), to);
        }

        private static TimeSpan ParseRange(string? range)
        {
            if (string.IsNullOrWhiteSpace(range) || range.Length < 2 || !int.TryParse(range[..^1], out int n))
                return TimeSpan.FromHours(1);

            return range[^1] switch
            {
                'm' => TimeSpan.FromMinutes(n),
                'h' => TimeSpan.FromHours(n),
                'd' => TimeSpan.FromDays(n),
                _ => TimeSpan.FromHours(1)
            };
        }
    }
}

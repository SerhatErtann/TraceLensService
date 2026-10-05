using TraceLensService.Enums;

namespace TraceLensService.Models.DbModels
{
    /// <summary>
    /// tracelens_alerts tablosundaki bir alarm. Güncellemeler aynı <see cref="Id"/> ile daha yüksek
    /// Version'la yeniden yazılır (ReplacingMergeTree).
    /// </summary>
    public sealed record AlertRecord(
        Guid Id,
        string Key,
        AppKind App,
        string Service,
        string Operation,
        string Metric,
        double ValueMs,
        double PeakValueMs,
        double ThresholdMs,
        long RequestCount,
        long SlowCount,
        DateTime FiredAt,
        DateTime LastCheckedAt,
        DateTime? ResolvedAt = null)
    {
        public double DurationMinutes => Math.Round(((ResolvedAt ?? LastCheckedAt) - FiredAt).TotalMinutes, 1);
    }
}

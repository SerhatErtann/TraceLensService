using TraceLensService.Enums;

namespace TraceLensService.Models.DbModels
{
    /// <summary>
    /// tracelens_alerts tablosundaki bir alarm. Güncellemeler aynı <see cref="Id"/> ile daha yüksek
    /// Version'la yeniden yazılır (ReplacingMergeTree).
    /// </summary>
    /// <param name="Kind">slow (ortalama/p95 süre eşiği aştı) | error (hata oranı sınırı aştı)</param>
    /// <param name="ValueMs">Son kontroldeki süre (hata alarmında da bilgi için ortalama süre).</param>
    /// <param name="TopStatus">Hata alarmında en sık sonuç: HTTP kodu ("500"), görevlerde hata tipi; yavaşlıkta boş.</param>
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
        DateTime? ResolvedAt = null,
        string Kind = AlertRecord.SlowKind,
        long ErrorCount = 0,
        double ErrorRate = 0,
        double PeakErrorRate = 0,
        string TopStatus = "")
    {
        public const string SlowKind = "slow";
        public const string ErrorKind = "error";

        public double DurationMinutes => Math.Round(((ResolvedAt ?? LastCheckedAt) - FiredAt).TotalMinutes, 1);
    }
}

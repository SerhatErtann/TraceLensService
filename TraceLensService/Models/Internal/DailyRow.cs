using TraceLensService.Enums;

namespace TraceLensService.Models.Internal
{
    /// <summary>
    /// Günlük özet tablosundan bir operasyonun (veya günün) toplamı. <see cref="Histogram"/>: aralık sırası → istek sayısı
    /// (GlobalConsts.HistogramEdgesMs; 1 = ilk sınırdan kısa, 0 = son sınırdan uzun); eşiği aşan sayısı bundan hesaplanır.
    /// </summary>
    public sealed record DailyRow(
        DateOnly? Day, AppKind App, string Service, string Operation,
        long Requests, double DurationSumMs, long Errors, Dictionary<int, long> Histogram,
        double P50Ms, double P90Ms, double P95Ms, double P99Ms);
}

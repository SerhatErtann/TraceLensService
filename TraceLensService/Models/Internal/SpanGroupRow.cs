namespace TraceLensService.Models.Internal
{
    /// <summary>Servis içindeki bir span grubunun (metod / DB sorgusu / dış çağrı) ham istatistiği.</summary>
    public sealed record SpanGroupRow(
        string Category, string Name, string Target, long Count,
        double AvgMs, double P95Ms, double MaxMs, long ErrorCount, double TotalMs);
}

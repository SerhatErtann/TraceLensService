using TraceLensService.Enums;

namespace TraceLensService.Models.Internal
{
    /// <summary>Bir servisin (veya Service boşsa tüm servislerin) seçili aralıktaki ham istatistiği.</summary>
    public sealed record ServiceStats(string Service, AppKind App, long Count, double AvgMs, double P95Ms, long ErrorCount, long SlowCount = 0)
    {
        public double ErrorRate => Count == 0 ? 0 : (double)ErrorCount / Count;
        public double SlowRate => Count == 0 ? 0 : (double)SlowCount / Count;
    }
}

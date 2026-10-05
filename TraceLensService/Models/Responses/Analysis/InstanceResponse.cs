namespace TraceLensService.Models.Responses.Analysis
{
    /// <summary>Aynı servisin çalışan bir kopyası (service.instance.id): kopyalardan biri diğerlerinden yavaş mı?</summary>
    public class InstanceResponse
    {
        public string InstanceId { get; set; } = string.Empty;
        /// <summary>host.name resource attribute'u varsa.</summary>
        public string? Host { get; set; }
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long SlowCount { get; set; }
        public long ErrorCount { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }

        public double ErrorRate => Count == 0 ? 0 : (double)ErrorCount / Count;
    }
}

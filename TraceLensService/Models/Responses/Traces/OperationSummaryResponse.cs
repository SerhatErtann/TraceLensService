namespace TraceLensService.Models.Responses.Traces
{
    /// <summary>Bir operasyonun (endpoint veya job) seçili zaman aralığındaki özeti.</summary>
    public class OperationSummaryResponse
    {
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public double MaxMs { get; set; }
        public long SlowCount { get; set; }
        public long ErrorCount { get; set; }
        public double ThresholdMs { get; set; }
        public DateTime LastSeen { get; set; }

        public bool IsAvgOverThreshold => AvgMs > ThresholdMs;
        public double ErrorRate => Count == 0 ? 0 : (double)ErrorCount / Count;
    }
}

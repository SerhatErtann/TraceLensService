namespace TraceLensService.Models.Responses.Traces
{
    public class TimeBucketResponse
    {
        public DateTime Time { get; set; }
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P50Ms { get; set; }
        public double P90Ms { get; set; }
        public double P95Ms { get; set; }
        public double P99Ms { get; set; }
        public long SlowCount { get; set; }
        public long ErrorCount { get; set; }
    }
}

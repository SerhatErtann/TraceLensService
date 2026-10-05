namespace TraceLensService.Models.Responses.Traces
{
    /// <summary>İstek listesindeki bir satır: servislerde gelen HTTP isteği, scheduler'larda job çalıştırması.</summary>
    public class RequestRowResponse
    {
        public DateTime Timestamp { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string SpanId { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public double DurationMs { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? StatusMessage { get; set; }
        public string? HttpStatusCode { get; set; }
        public long? RequestBytes { get; set; }
        public long? ResponseBytes { get; set; }
        public string? JobStatus { get; set; }
    }
}

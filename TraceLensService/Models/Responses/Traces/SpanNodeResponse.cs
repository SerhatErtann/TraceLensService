namespace TraceLensService.Models.Responses.Traces
{
    public class SpanNodeResponse
    {
        public string SpanId { get; set; } = string.Empty;
        public string? ParentSpanId { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        /// <summary>Trace başlangıcından itibaren ms.</summary>
        public double StartOffsetMs { get; set; }
        public double DurationMs { get; set; }

        /// <summary>Alt span'lerin kapladığı süre çıkarılmış, span'in kendi harcadığı süre.</summary>
        public double SelfMs { get; set; }
        public int Depth { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? StatusMessage { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = [];
        public List<SpanEventResponse> Events { get; set; } = [];
    }
}

namespace TraceLensService.Models.Responses.Traces
{
    public class TraceDetailResponse
    {
        public string TraceId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public double DurationMs { get; set; }
        public int SpanCount { get; set; }
        public List<string> Services { get; set; } = [];

        /// <summary>Waterfall sırasıyla (DFS) span'ler.</summary>
        public List<SpanNodeResponse> Spans { get; set; } = [];

        /// <summary>Kök neden ipuçları: "nereye bakmalıyım?" sorusunun cevabı.</summary>
        public List<TraceHintResponse> Hints { get; set; } = [];
    }
}

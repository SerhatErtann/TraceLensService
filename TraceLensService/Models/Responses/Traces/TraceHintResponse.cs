namespace TraceLensService.Models.Responses.Traces
{
    public class TraceHintResponse
    {
        /// <summary>slow-span, n-plus-one, large-payload, error</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>info, warning, error</summary>
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? SpanId { get; set; }
    }
}

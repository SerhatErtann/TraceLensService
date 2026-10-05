namespace TraceLensService.Models.Responses.Traces
{
    /// <summary>Span içindeki olay; en önemlisi "exception" (tip, mesaj, stack trace).</summary>
    public class SpanEventResponse
    {
        public DateTime Timestamp { get; set; }
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, string> Attributes { get; set; } = [];
    }
}

using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Models.Internal
{
    /// <summary>otel_traces'ten okunan ham span; TraceAnalyzer bunlardan waterfall ağacını kurar.</summary>
    public sealed record RawSpan(
        DateTime Timestamp,
        string SpanId,
        string? ParentSpanId,
        string Service,
        string Name,
        string Kind,
        double DurationMs,
        string Status,
        string? StatusMessage,
        Dictionary<string, string> Attributes,
        List<SpanEventResponse> Events);
}

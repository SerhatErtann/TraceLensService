namespace TraceLensService.Models.Internal
{
    /// <summary>İstek anatomisi için servisin bir span'i: türü (root, method, db, call, other) ve okunur adıyla.</summary>
    public sealed record AnatomySpan(
        string TraceId, string SpanId, string? ParentSpanId, double DurationMs, string Category, string Name, string Target);
}

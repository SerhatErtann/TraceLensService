using TraceLensService.Enums;

namespace TraceLensService.Models.Internal
{
    /// <summary>Sorgulara giden çözümlenmiş filtre (zaman aralığı hesaplanmış hali).</summary>
    public sealed record TraceFilter(
        AppKind App,
        DateTimeOffset From,
        DateTimeOffset To,
        string? Service = null,
        string? Operation = null,
        double? MinDurationMs = null,
        bool OnlyErrors = false,
        bool OnlySlow = false);
}

using System.Diagnostics;

namespace Sample.OrderService;

/// <summary>Kendi metodlarımızı waterfall'da ayrı adım olarak görmek için. AddSource(SourceName) ile kaydedilir.</summary>
public static class Tracing
{
    public const string SourceName = "TraceLens";

    public static readonly ActivitySource Source = new(SourceName);
}

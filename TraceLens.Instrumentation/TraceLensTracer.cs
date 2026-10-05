using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TraceLens.Instrumentation;

public static class TraceLensTracer
{
    public const string SourceName = "TraceLens";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// Bulunduğu metod için bir alt span açar; waterfall'da "SinifAdi.MetodAdi" olarak görünür.
    /// <code>using var span = TraceLensTracer.StartMethod();</code>
    /// </summary>
    public static Activity? StartMethod(
        [CallerMemberName] string method = "",
        [CallerFilePath] string file = "")
    {
        var className = Path.GetFileNameWithoutExtension(file);
        return Source.StartActivity($"{className}.{method}");
    }
}

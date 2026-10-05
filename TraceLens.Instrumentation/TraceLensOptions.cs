namespace TraceLens.Instrumentation;

public enum AppType
{
    Service,
    Scheduler
}

public sealed class TraceLensOptions
{
    public const string SectionName = "TraceLens";

    /// <summary>Dashboard'da görünecek uygulama adı. Boşsa assembly adı kullanılır.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Uygulamanın Services mi Schedulers sayfasında listeleneceğini belirler.</summary>
    public AppType AppType { get; set; } = AppType.Service;

    public string Environment { get; set; } = "development";

    /// <summary>OTel Collector OTLP/gRPC adresi.</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>Bu path'lerle başlayan istekler trace edilmez.</summary>
    public string[] ExcludedPaths { get; set; } = ["/health", "/metrics", "/swagger"];

    /// <summary>EF Core sorgularını span olarak kaydeder.</summary>
    public bool UseEntityFrameworkCore { get; set; } = true;

    /// <summary>
    /// Ham SqlClient (Dapper, ADO.NET) sorgularını kaydeder. EF Core ile birlikte açılırsa
    /// SQL Server sorguları iki kez görünür; EF kullanmayan uygulamalarda açın.
    /// </summary>
    public bool UseSqlClient { get; set; }
}

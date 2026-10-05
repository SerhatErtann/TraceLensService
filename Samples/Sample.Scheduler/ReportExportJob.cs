using TraceLens.Instrumentation;

namespace Sample.Scheduler;

/// <summary>Raporu indirir; yavaş ve büyük yanıtlı bir job örneği. Ara sıra hata verir.</summary>
public sealed class ReportExportJob(IJobTracer jobTracer, ILogger<ReportExportJob> logger, IHttpClientFactory http)
    : TracedBackgroundService(jobTracer, logger)
{
    protected override string JobName => "ReportExport";
    protected override TimeSpan Interval => TimeSpan.FromSeconds(15);

    protected override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        var report = await http.CreateClient("orders").GetStringAsync("/orders/report", cancellationToken);

        using var span = TraceLensTracer.StartMethod();
        span?.SetTag("report.bytes", report.Length);

        if (Random.Shared.Next(100) < 15)
            throw new IOException("Rapor dosya sunucusuna yazılamadı");
    }
}

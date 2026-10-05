namespace Sample.Scheduler;

/// <summary>Raporu indirir; yavaş ve büyük yanıtlı bir job örneği. Ara sıra hata verir.</summary>
public sealed class ReportExportJob(ILogger<ReportExportJob> logger, IHttpClientFactory http) : BackgroundService
{
    private const string JobName = "ReportExport";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await JobTracing.RunAsync(JobName, RunAsync, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Job {JobName} başarısız oldu", JobName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var report = await http.CreateClient("orders").GetStringAsync("/orders/report", cancellationToken);

        if (Random.Shared.Next(100) < 15)
            throw new IOException($"Rapor ({report.Length} bayt) dosya sunucusuna yazılamadı");
    }
}

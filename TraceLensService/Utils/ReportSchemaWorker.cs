using TraceLensService.Contexts;

namespace TraceLensService.Utils
{
    /// <summary>Açılışta raporların günlük özet tablosunu ve onu dolduran view'i hazırlar (ClickHouse hazır olana kadar dener).</summary>
    public class ReportSchemaWorker(ReportQueries queries, ILogger<ReportSchemaWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (int attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    await queries.EnsureSchemaAsync(stoppingToken);
                    return;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Rapor tablosu hazırlanamadı (deneme {Attempt}): {Message}", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * attempt, 30)), stoppingToken);
                }
            }
        }
    }
}

using TraceLensService.Common;
using TraceLensService.Contexts;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Açılışta eşik tablosunu hazırlar (gerekirse config'ten doldurur), sonra periyodik olarak yeniler;
    /// böylece birden fazla TraceLensService örneği aynı eşikleri görür.
    /// </summary>
    public class ThresholdSyncWorker(ThresholdQueries queries, ThresholdStore store, ILogger<ThresholdSyncWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (int attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    await queries.EnsureSchemaAsync(stoppingToken);
                    await store.SeedFromConfigIfEmptyAsync(stoppingToken);
                    await store.ReloadAsync(stoppingToken);
                    logger.LogInformation("Eşikler yüklendi: varsayılan {Default} ms, {Count} özel eşik",
                        store.Current.DefaultMs, store.Overrides.Count);
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Eşikler yüklenemedi (deneme {Attempt}): {Message}", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * attempt, 30)), stoppingToken);
                }
            }

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(GlobalConsts.ThresholdRefreshSeconds));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await store.ReloadAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Eşikler yenilenemedi: {Message}", ex.Message);
                }
            }
        }
    }
}

using Microsoft.Extensions.Options;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Belirli aralıklarla son N dakikayı tarar; eşiği aşan operasyonlar için alarm açar, düzelenleri kapatır.
    /// </summary>
    public class AlertWorker(
        TraceQueries traces,
        AlertQueries alerts,
        ActiveAlertCache cache,
        AlertNotifier notifier,
        IOptionsMonitor<AlertOptions> options,
        ILogger<AlertWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await InitializeAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                AlertOptions opts = options.CurrentValue;
                if (opts.Enabled)
                {
                    try
                    {
                        await CheckAsync(opts, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Alarm kontrolü başarısız");
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(opts.CheckIntervalSeconds, 5)), stoppingToken);
            }
        }

        /// <summary>ClickHouse hazır olana kadar bekler; tabloyu oluşturur ve açık alarmları belleğe alır.</summary>
        private async Task InitializeAsync(CancellationToken ct)
        {
            for (int attempt = 1; !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    await alerts.EnsureSchemaAsync(ct);
                    List<AlertRecord> active = await alerts.GetActiveAsync(ct);
                    cache.Load(active);
                    logger.LogInformation("Alarm deposu hazır; {Count} açık alarm yüklendi", active.Count);
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning("ClickHouse'a bağlanılamadı (deneme {Attempt}): {Message}", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * attempt, 30)), ct);
                }
            }
        }

        private async Task CheckAsync(AlertOptions opts, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            HashSet<string> firing = [];
            bool usePercentile = opts.Metric.Equals("p95", StringComparison.OrdinalIgnoreCase);
            string metric = usePercentile ? "p95" : "avg";

            DateTime windowStart = now.AddMinutes(-opts.WindowMinutes);
            Dictionary<(string, string), string> topStatus = opts.ErrorAlertsEnabled
                ? await traces.GetTopErrorStatusAsync(windowStart, now, ct)
                : [];

            foreach (AppKind app in Enum.GetValues<AppKind>())
            {
                List<OperationSummaryResponse> summary = await traces.GetSummaryAsync(
                    new TraceFilter(app, windowStart, now), ct);

                foreach (OperationSummaryResponse row in summary.Where(r => r.Count >= opts.MinRequestCount))
                {
                    if (opts.ErrorAlertsEnabled)
                        await CheckErrorsAsync(opts, app, row, topStatus.GetValueOrDefault((row.Service, row.Operation), string.Empty), firing, now, ct);

                    double value = usePercentile ? row.P95Ms : row.AvgMs;
                    string key = $"{app}|{row.Service}|{row.Operation}";

                    // Açılış eşiği = threshold; açık alarm için kapanış eşiği daha düşük (histerezis).
                    double limit = cache.IsActive(key) ? row.ThresholdMs * opts.ResolveRatio : row.ThresholdMs;
                    if (value <= limit) continue;

                    firing.Add(key);
                    (AlertRecord alert, bool isNew) = cache.Upsert(key, app, row, metric, value, now);
                    await alerts.SaveAsync(alert, ct);

                    if (isNew)
                    {
                        logger.LogWarning("ALARM {Service} {Operation}: {Metric}={Value}ms > {Threshold}ms ({Count} istek)",
                            row.Service, row.Operation, metric, value, row.ThresholdMs, row.Count);
                        await notifier.NotifyAsync(AlertNotifier.Fired, alert, ct);
                    }
                }
            }

            foreach (AlertRecord resolved in cache.ResolveMissing(firing, now))
            {
                await alerts.SaveAsync(resolved, ct);
                logger.LogInformation("Alarm kapandı: {Service} {Operation} ({Minutes} dk sürdü)",
                    resolved.Service, resolved.Operation, resolved.DurationMinutes);
                await notifier.NotifyAsync(AlertNotifier.Resolved, resolved, ct);
            }
        }

        /// <summary>
        /// Hata alarmı: pencerede hata oranı sınırı aştıysa (ve tek tük değilse) açılır; açıkken oran sınırın
        /// ResolveRatio katının altına inince kapanır. Anahtarı yavaşlık alarmından ayrıdır, ikisi aynı anda açık olabilir.
        /// </summary>
        private async Task CheckErrorsAsync(AlertOptions opts, AppKind app, OperationSummaryResponse row, string topStatus,
            HashSet<string> firing, DateTime now, CancellationToken ct)
        {
            string key = $"{AlertRecord.ErrorKind}|{app}|{row.Service}|{row.Operation}";
            bool active = cache.IsActive(key);
            double limit = active ? opts.ErrorRate * opts.ResolveRatio : opts.ErrorRate;
            if (row.ErrorRate < limit || (!active && row.ErrorCount < opts.MinErrorCount)) return;

            firing.Add(key);
            (AlertRecord alert, bool isNew) = cache.UpsertError(key, app, row, topStatus, now);
            await alerts.SaveAsync(alert, ct);
            if (isNew)
            {
                logger.LogWarning("HATA ALARMI {Service} {Operation}: hata oranı {Rate:P1} ({Errors}/{Count}), en sık {Status}",
                    row.Service, row.Operation, row.ErrorRate, row.ErrorCount, row.Count, topStatus);
                await notifier.NotifyAsync(AlertNotifier.Fired, alert, ct);
            }
        }
    }
}

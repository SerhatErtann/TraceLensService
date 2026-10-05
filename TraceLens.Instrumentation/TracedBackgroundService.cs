using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TraceLens.Instrumentation;

/// <summary>
/// Belirli aralıklarla çalışan ve her çalıştırmayı trace eden BackgroundService tabanı.
/// Bir job hata verirse loglanır ve bir sonraki tick'te tekrar denenir.
/// </summary>
public abstract class TracedBackgroundService(IJobTracer jobTracer, ILogger logger) : BackgroundService
{
    protected abstract string JobName { get; }

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunJobAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await jobTracer.RunAsync(JobName, RunJobAsync, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Job {JobName} başarısız oldu", JobName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

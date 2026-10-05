namespace Sample.Scheduler;

/// <summary>Sipariş listesini çekip birkaçının detayına bakar (OrderService → PaymentService zinciri).</summary>
public sealed class OrderSyncJob(ILogger<OrderSyncJob> logger, IHttpClientFactory http) : BackgroundService
{
    private const string JobName = "OrderSync";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
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
        var client = http.CreateClient("orders");
        await client.GetStringAsync("/orders", cancellationToken);

        for (var i = 0; i < 3; i++)
        {
            var id = Random.Shared.Next(1, 51);
            using var response = await client.GetAsync($"/orders/{id}/details", cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Sipariş {OrderId} detayı alınamadı: {Status}", id, response.StatusCode);
        }
    }
}

using TraceLens.Instrumentation;

namespace Sample.Scheduler;

/// <summary>Sipariş listesini çekip birkaçının detayına bakar (OrderService → PaymentService zinciri).</summary>
public sealed class OrderSyncJob(IJobTracer jobTracer, ILogger<OrderSyncJob> logger, IHttpClientFactory http)
    : TracedBackgroundService(jobTracer, logger)
{
    protected override string JobName => "OrderSync";
    protected override TimeSpan Interval => TimeSpan.FromSeconds(5);

    protected override async Task RunJobAsync(CancellationToken cancellationToken)
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

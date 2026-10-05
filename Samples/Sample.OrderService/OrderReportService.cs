using Microsoft.EntityFrameworkCore;

namespace Sample.OrderService;

public sealed class OrderReportService(OrderDb db)
{
    public async Task<object> BuildAsync()
    {
        using var span = Tracing.Source.StartActivity("OrderReportService.BuildAsync");

        var lines = await LoadLinesAsync();
        var totals = await CalculateTotalsAsync(lines);
        return new { generatedAt = DateTime.UtcNow, totals, lines };
    }

    private async Task<List<OrderLine>> LoadLinesAsync()
    {
        using var span = Tracing.Source.StartActivity("OrderReportService.LoadLinesAsync");
        return await db.OrderLines.ToListAsync();
    }

    private static async Task<Dictionary<int, int>> CalculateTotalsAsync(List<OrderLine> lines)
    {
        using var span = Tracing.Source.StartActivity("OrderReportService.CalculateTotalsAsync");
        span?.SetTag("report.line_count", lines.Count);

        // Pahalı bir hesaplamayı simüle eder: çoğu zaman hızlı, %30 ihtimalle yavaş (waterfall'da en uzun adım).
        await Task.Delay(Random.Shared.Next(100) < 30 ? Random.Shared.Next(250, 450) : Random.Shared.Next(30, 120));
        return lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
    }
}

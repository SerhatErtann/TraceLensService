using Microsoft.EntityFrameworkCore;
using TraceLens.Instrumentation;

namespace Sample.OrderService;

public sealed class OrderReportService(OrderDb db)
{
    public async Task<object> BuildAsync()
    {
        using var span = TraceLensTracer.StartMethod();

        var lines = await LoadLinesAsync();
        var totals = await CalculateTotalsAsync(lines);
        return new { generatedAt = DateTime.UtcNow, totals, lines };
    }

    private async Task<List<OrderLine>> LoadLinesAsync()
    {
        using var span = TraceLensTracer.StartMethod();
        return await db.OrderLines.ToListAsync();
    }

    private static async Task<Dictionary<int, int>> CalculateTotalsAsync(List<OrderLine> lines)
    {
        using var span = TraceLensTracer.StartMethod();
        span?.SetTag("report.line_count", lines.Count);

        // Pahalı bir hesaplamayı simüle eder: waterfall'da en uzun span bu olacak.
        await Task.Delay(Random.Shared.Next(250, 450));
        return lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
    }
}

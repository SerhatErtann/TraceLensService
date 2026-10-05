using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sample.OrderService;

var builder = WebApplication.CreateBuilder(args);

// ---- TraceLens: istek süreleri ve trace'ler (README → "Bir servisi TraceLens'e bağlamak") ----
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(builder.Configuration["TraceLens:ServiceName"]!)
        .AddAttributes([new("tracelens.app_type", "service")]))
    .WithTracing(t => t
        .AddSource(Tracing.SourceName)
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["TraceLens:OtlpEndpoint"]!)));

builder.Services.AddDbContext<OrderDb>(o => o.UseSqlite("Data Source=orders.db"));
builder.Services.AddScoped<OrderReportService>();
builder.Services.AddHttpClient("payment", c =>
    c.BaseAddress = new Uri(builder.Configuration["PaymentServiceUrl"] ?? "http://localhost:5102"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await OrderDb.SeedAsync(scope.ServiceProvider.GetRequiredService<OrderDb>());

app.MapGet("/health", () => "ok");

// Hızlı endpoint: tek sorgu
app.MapGet("/orders", async (OrderDb db) =>
    await db.Orders.OrderByDescending(o => o.Id).Take(20).ToListAsync());

// Kötü örnek: N+1 sorgu + başka servise HTTP çağrısı
app.MapGet("/orders/{id:int}/details", async (int id, OrderDb db, IHttpClientFactory http) =>
{
    var order = await db.Orders.FindAsync(id);
    if (order is null) return Results.NotFound();

    var lines = new List<OrderLine>();
    foreach (var lineId in await db.OrderLines.Where(l => l.OrderId == id).Select(l => l.Id).ToListAsync())
        lines.Add((await db.OrderLines.FindAsync(lineId))!);

    var payment = await http.CreateClient("payment").GetStringAsync($"/payments/{id}/status");
    return Results.Ok(new { order, lines, payment });
});

// Yavaş + şişkin yanıt: metod span'leri ve büyük payload
app.MapGet("/orders/report", async (OrderReportService reports) => await reports.BuildAsync());

app.Run();

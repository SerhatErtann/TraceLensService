using Microsoft.EntityFrameworkCore;
using Sample.OrderService;
using TraceLens.Instrumentation;

var builder = WebApplication.CreateBuilder(args);

// 1) Tek satır: tracing + exporter
builder.Services.AddTraceLens(builder.Configuration);

builder.Services.AddDbContext<OrderDb>(o => o.UseSqlite("Data Source=orders.db"));
builder.Services.AddScoped<OrderReportService>();
builder.Services.AddHttpClient("payment", c =>
    c.BaseAddress = new Uri(builder.Configuration["PaymentServiceUrl"] ?? "http://localhost:5102"));

var app = builder.Build();

// 2) Payload boyutu middleware'i
app.UseTraceLens();

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

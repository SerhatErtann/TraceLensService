using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---- TraceLens: istek süreleri ve trace'ler (README → "Bir servisi TraceLens'e bağlamak") ----
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(builder.Configuration["TraceLens:ServiceName"]!)
        .AddAttributes([new("tracelens.app_type", "service")]))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["TraceLens:OtlpEndpoint"]!)));

var app = builder.Build();

app.MapGet("/health", () => "ok");

// Değişken gecikme ve ara sıra hata: dashboard'da yavaş/hatalı istek örnekleri üretir.
app.MapGet("/payments/{orderId:int}/status", async (int orderId) =>
{
    await Task.Delay(Random.Shared.Next(20, 400));

    if (Random.Shared.Next(100) < 8)
        return Results.Problem("Banka servisi yanıt vermedi", statusCode: 502);

    return Results.Ok(new { orderId, status = "paid" });
});

app.Run();

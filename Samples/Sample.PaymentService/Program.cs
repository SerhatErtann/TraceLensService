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

// Gerçekçi dağılım: çoğu istek hızlı, bir kısmı yavaş (eşiği aşar), az bir kısmı hata verir.
// Dashboard'da normal / turuncu (yavaş) / kırmızı (hatalı) satırların hepsi görünsün diye.
app.MapGet("/payments/{orderId:int}/status", async (int orderId) =>
{
    int roll = Random.Shared.Next(100);

    if (roll < 5)
    {
        await Task.Delay(Random.Shared.Next(15, 60));
        return Results.Problem("Banka servisi yanıt vermedi", statusCode: 502);
    }

    // %10 yavaş (250-600 ms), geri kalanı hızlı (20-120 ms)
    await Task.Delay(roll < 15 ? Random.Shared.Next(250, 600) : Random.Shared.Next(20, 120));
    return Results.Ok(new { orderId, status = "paid" });
});

app.Run();

using TraceLens.Instrumentation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTraceLens(builder.Configuration);

var app = builder.Build();
app.UseTraceLens();

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

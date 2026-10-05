using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sample.Scheduler;

var builder = Host.CreateApplicationBuilder(args);

// ---- TraceLens: job süreleri ve trace'ler (README → "Bir servisi TraceLens'e bağlamak") ----
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(builder.Configuration["TraceLens:ServiceName"]!)
        .AddAttributes([new("tracelens.app_type", "scheduler")]))
    .WithTracing(t => t
        .AddSource(JobTracing.SourceName)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["TraceLens:OtlpEndpoint"]!)));

builder.Services.AddHttpClient("orders", c =>
    c.BaseAddress = new Uri(builder.Configuration["OrderServiceUrl"] ?? "http://localhost:5101"));

builder.Services.AddHostedService<OrderSyncJob>();
builder.Services.AddHostedService<ReportExportJob>();

builder.Build().Run();

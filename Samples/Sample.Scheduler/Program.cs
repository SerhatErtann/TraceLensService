using Sample.Scheduler;
using TraceLens.Instrumentation;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTraceLens(builder.Configuration);
builder.Services.AddHttpClient("orders", c =>
    c.BaseAddress = new Uri(builder.Configuration["OrderServiceUrl"] ?? "http://localhost:5101"));

builder.Services.AddHostedService<OrderSyncJob>();
builder.Services.AddHostedService<ReportExportJob>();

builder.Build().Run();

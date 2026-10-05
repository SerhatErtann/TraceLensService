using CommonUtils.Extensions;
using CommonUtils.Interfaces;
using System.Net;
using System.Text.Json.Serialization;
using TraceLensService.Business;
using TraceLensService.Common;
using TraceLensService.Contexts;
using TraceLensService.Endpoints;
using TraceLensService.Models.Options;
using TraceLensService.Utils;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(GlobalConsts.ConfigLine, optional: false, reloadOnChange: true);
// Ortam değişkenleri config dosyasını ezebilsin (ör. Notifications__WebhookUrl gibi gizli değerler).
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddCors(options =>
{
    options.AddPolicy(GlobalConsts.DefaultCorsPolicy, policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.Configure<ThresholdOptions>(builder.Configuration.GetSection(ThresholdOptions.SectionName));
builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection(AlertOptions.SectionName));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));

// ---- ClickHouse ----
builder.Services.AddHttpClient(GlobalConsts.ClickHouseHttpClient)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // ClickHouse yanıtları sıkıştırılmış gönderir
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });
builder.Services.AddHttpClient(GlobalConsts.WebhookHttpClient, c => c.Timeout = TimeSpan.FromSeconds(GlobalConsts.WebhookTimeoutSeconds));
builder.Services.AddSingleton<ClickHouseContext>();
builder.Services.AddSingleton<TraceQueries>();
builder.Services.AddSingleton<AlertQueries>();
builder.Services.AddSingleton<ThresholdQueries>();

// ---- Eşikler (ClickHouse'tan yüklenir, dashboard'dan yönetilir) ----
builder.Services.AddSingleton<ThresholdStore>();
builder.Services.AddHostedService<ThresholdSyncWorker>();

// ---- Alarmlar ----
builder.Services.AddSingleton<ActiveAlertCache>();
builder.Services.AddSingleton<AlertNotifier>();
builder.Services.AddHostedService<AlertWorker>();

builder.Services.AddScoped<IEndpoint, TraceLensEndpoints>();
builder.Services.AddScoped<TraceBusiness>();
builder.Services.AddScoped<AlertBusiness>();
builder.Services.AddScoped<ThresholdBusiness>();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>(GlobalConsts.SwaggerIsEnabled))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(GlobalConsts.DefaultCorsPolicy);
app.UseRouting();
app.MapEndpoints();
// Container/yük dengeleyici sağlık kontrolü için
app.MapGet(GlobalConsts.HealthUrl, () => Results.Ok("ok")).ExcludeFromDescription();
app.Run();

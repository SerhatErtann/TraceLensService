using CommonUtils.Extensions;
using CommonUtils.Interfaces;
using CommonUtils.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
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
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<ReportOptions>(builder.Configuration.GetSection(ReportOptions.SectionName));

// ---- Giriş (kullanıcı adı + şifre, cookie oturumu) ----
AuthOptions authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
if (authOptions.Required && !authOptions.Enabled)
    throw new InvalidOperationException(GlobalConsts.GeneralConsts.AuthPasswordMissing);

builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = GlobalConsts.AuthCookieName;
        o.Cookie.HttpOnly = true;
        // Strict: başka sitelerden gelen isteklere cookie eklenmez (CSRF koruması)
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
        o.SlidingExpiration = true;
        // API: oturum yoksa login sayfasına yönlendirmek yerine 401 dön; dashboard giriş ekranını kendisi açar
        o.Events.OnRedirectToLogin = ctx => WriteFailure(ctx.Response, StatusCodes.Status401Unauthorized, GlobalConsts.GeneralConsts.LoginRequired);
        o.Events.OnRedirectToAccessDenied = ctx => WriteFailure(ctx.Response, StatusCodes.Status403Forbidden, GlobalConsts.GeneralConsts.LoginRequired);
    });
builder.Services.AddAuthorization(o =>
{
    // Şifre tanımlıysa AllowAnonymous olmayan her uç oturum ister
    if (authOptions.Enabled)
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(GlobalConsts.LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = GlobalConsts.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = (ctx, _) => new ValueTask(WriteFailure(ctx.HttpContext.Response, StatusCodes.Status429TooManyRequests, GlobalConsts.GeneralConsts.TooManyLoginAttempts));
});
// nginx arkasında gerçek istemci IP'si (deneme sınırı IP bazlı). Servis yalnızca Docker ağından erişilebilir.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

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
builder.Services.AddSingleton<ReportQueries>();

// ---- Eşikler (ClickHouse'tan yüklenir, dashboard'dan yönetilir) ----
builder.Services.AddSingleton<ThresholdStore>();
builder.Services.AddHostedService<ThresholdSyncWorker>();
builder.Services.AddHostedService<ReportSchemaWorker>();

// ---- Alarmlar ----
builder.Services.AddSingleton<ActiveAlertCache>();
builder.Services.AddSingleton<AlertNotifier>();
builder.Services.AddHostedService<AlertWorker>();

builder.Services.AddScoped<IEndpoint, TraceLensEndpoints>();
builder.Services.AddScoped<TraceBusiness>();
builder.Services.AddScoped<AlertBusiness>();
builder.Services.AddScoped<ThresholdBusiness>();
builder.Services.AddScoped<AuthBusiness>();
builder.Services.AddScoped<OverviewBusiness>();
builder.Services.AddScoped<LiveBusiness>();
builder.Services.AddScoped<ReportBusiness>();

var app = builder.Build();

if (!authOptions.Enabled)
    app.Logger.LogWarning("Dashboard girişi KAPALI (Auth:Password tanımlı değil). Sunucuda mutlaka şifre verin.");

app.UseForwardedHeaders();

if (builder.Configuration.GetValue<bool>(GlobalConsts.SwaggerIsEnabled))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(GlobalConsts.DefaultCorsPolicy);
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();
// Container/yük dengeleyici sağlık kontrolü için
app.MapGet(GlobalConsts.HealthUrl, () => Results.Ok("ok")).AllowAnonymous().ExcludeFromDescription();
app.Run();

// Yetkisiz/sınır aşımı yanıtları da diğer uçlar gibi BaseResponse gövdesiyle döner.
static Task WriteFailure(HttpResponse response, int statusCode, string message)
{
    BaseResponse body = new();
    body.Failure(message);
    response.StatusCode = statusCode;
    return response.WriteAsJsonAsync(body);
}

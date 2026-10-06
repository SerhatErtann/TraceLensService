using CommonUtils.Extensions;
using CommonUtils.Interfaces;
using CommonUtils.Models;
using Microsoft.AspNetCore.Authentication;
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
using TraceLensService.Models.DbModels;
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
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

// ---- Giriş (kullanıcılar tracelens_users tablosunda, cookie oturumu) ----
AuthOptions authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();

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
        // Silinen kullanıcının ya da şifresi değişen hesabın eski oturumları hemen düşer
        o.Events.OnValidatePrincipal = async ctx =>
        {
            UserStore users = ctx.HttpContext.RequestServices.GetRequiredService<UserStore>();
            UserRecord? user;
            try
            {
                user = await users.FindAsync(ctx.Principal?.Identity?.Name, ctx.HttpContext.RequestAborted);
            }
            catch (Exception)
            {
                return; // ClickHouse geçici olarak erişilemiyorsa oturum düşürülmez
            }
            if (user is null || ctx.Principal?.FindFirst(GlobalConsts.PasswordStampClaim)?.Value != PasswordHasher.Stamp(user.PasswordHash))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
// login/register/logout/me (AllowAnonymous) dışındaki her uç oturum ister
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
AiOptions aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new();
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(GlobalConsts.LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = GlobalConsts.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(GlobalConsts.AssistantRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, aiOptions.QuestionsPerMinute), Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = (ctx, _) => new ValueTask(WriteFailure(ctx.HttpContext.Response, StatusCodes.Status429TooManyRequests,
        ctx.HttpContext.Request.Path.StartsWithSegments("/" + GlobalConsts.AssistantPathPrefix)
            ? GlobalConsts.GeneralConsts.TooManyQuestions
            : GlobalConsts.GeneralConsts.TooManyLoginAttempts));
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
builder.Services.AddSingleton<UserQueries>();
builder.Services.AddSingleton<UserStore>();

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
builder.Services.AddScoped<AssistantTools>();
builder.Services.AddScoped<AssistantBusiness>();

var app = builder.Build();

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

// Sadece geliştirme: asistan aracının Claude'a ne döndüğünü API anahtarı olmadan görmek için
// GET /api/v1/assistant/tools/get_overview?input={"range":"1h"}
if (app.Environment.IsDevelopment())
{
    app.MapGet("/" + GlobalConsts.AssistantPathPrefix + "/tools/{name}", async (string name, string? input, AssistantTools tools) =>
    {
        Dictionary<string, System.Text.Json.JsonElement> args =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(input ?? "{}") ?? [];
        (string content, bool isError) = await tools.ExecuteAsync(name, args);
        return Results.Text(content, isError ? "text/plain" : "application/json");
    }).ExcludeFromDescription();
}
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

using CommonUtils.Interfaces;
using CommonUtils.Models;
using Microsoft.AspNetCore.Mvc;
using TraceLensService.Business;
using TraceLensService.Common;
using TraceLensService.Enums;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Alerts;
using TraceLensService.Models.Responses.Auth;
using TraceLensService.Models.Responses.Settings;
using TraceLensService.Models.Responses.Shared;
using TraceLensService.Models.Responses.Thresholds;
using TraceLensService.Models.Responses.Traces;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Endpoints
{
    public class TraceLensEndpoints : IEndpoint
    {
        public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            #region Services / Schedulers

            // İki sayfa aynı uçları kullanır; yalnızca "kök span" tanımı (HTTP isteği vs job) değişir.
            MapOverviewEndpoints(endpoints.MapGroup(TraceLensRouteUrls.ServiceGroup).WithTags(GeneralConsts.Services), AppKind.Service);
            MapOverviewEndpoints(endpoints.MapGroup(TraceLensRouteUrls.SchedulerGroup).WithTags(GeneralConsts.Schedulers), AppKind.Scheduler);

            #endregion

            RouteGroupBuilder api = endpoints.MapGroup(TraceLensRouteUrls.ApiVersion);

            #region Auth

            // Diğer tüm uçlar Program.cs'teki varsayılan politika ile oturum ister; bu üçü herkese açık.
            api.MapPost(TraceLensRouteUrls.AuthLogin, async ([FromServices] AuthBusiness business, [FromBody] LoginRequest request) =>
            {
                DataResponse<AuthStatusResponse> response = await business.Login(request);
                return Results.Ok(response);
            })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithDescription("Kullanıcı adı + şifre ile oturum açar (cookie)")
            .Produces<DataResponse<AuthStatusResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Auth);

            api.MapPost(TraceLensRouteUrls.AuthLogout, async ([FromServices] AuthBusiness business) =>
            {
                BaseResponse response = await business.Logout();
                return Results.Ok(response);
            })
            .AllowAnonymous()
            .WithDescription("Oturumu kapatır")
            .Produces<BaseResponse>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Auth);

            api.MapGet(TraceLensRouteUrls.AuthStatus, async ([FromServices] AuthBusiness business) =>
            {
                DataResponse<AuthStatusResponse> response = await business.GetStatus();
                return Results.Ok(response);
            })
            .AllowAnonymous()
            .WithDescription("Giriş açık mı ve oturum var mı")
            .Produces<DataResponse<AuthStatusResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Auth);

            #endregion

            #region Trace

            api.MapGet(TraceLensRouteUrls.TraceDetail, async ([FromServices] TraceBusiness business, string traceId) =>
            {
                DataResponse<TraceDetailResponse> response = await business.GetTrace(traceId);
                return Results.Ok(response);
            })
            .WithDescription("Trace waterfall'ı, self-time ve kök neden ipuçları")
            .Produces<DataResponse<TraceDetailResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Traces);

            #endregion

            #region Alerts

            api.MapGet(TraceLensRouteUrls.Alerts, async ([FromServices] AlertBusiness business, [FromQuery] int? days) =>
            {
                DataResponse<AlertListResponse> response = await business.GetAlerts(days);
                return Results.Ok(response);
            })
            .WithDescription("Açık alarmlar ve son N günün kapanan alarmları")
            .Produces<DataResponse<AlertListResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Alerts);

            api.MapPost(TraceLensRouteUrls.AlertTestNotification, async ([FromServices] AlertBusiness business) =>
            {
                BaseResponse response = await business.SendTestNotification();
                return Results.Ok(response);
            })
            .WithDescription("Ayarlı webhook'a örnek bir alarm bildirimi gönderir")
            .Produces<BaseResponse>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Alerts);

            #endregion

            #region Settings

            api.MapGet(TraceLensRouteUrls.Settings, async ([FromServices] AlertBusiness business) =>
            {
                DataResponse<SettingsResponse> response = await business.GetSettings();
                return Results.Ok(response);
            })
            .WithDescription("Eşik ve alarm ayarları (dashboard'un eşik çizgisi için)")
            .Produces<DataResponse<SettingsResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Settings);

            #endregion

            #region Thresholds

            api.MapGet(TraceLensRouteUrls.Thresholds, async ([FromServices] ThresholdBusiness business) =>
            {
                DataResponse<ThresholdListResponse> response = await business.GetThresholds();
                return Results.Ok(response);
            })
            .WithDescription("Varsayılan eşik ve operasyon bazlı özel eşikler")
            .Produces<DataResponse<ThresholdListResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Thresholds);

            api.MapPut(TraceLensRouteUrls.ThresholdDefault, async ([FromServices] ThresholdBusiness business,
                [FromBody] DefaultThresholdRequest request) =>
            {
                DataResponse<ThresholdListResponse> response = await business.SetDefault(request);
                return Results.Ok(response);
            })
            .WithDescription("Varsayılan eşiği değiştirir; anında geçerli olur")
            .Produces<DataResponse<ThresholdListResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Thresholds);

            api.MapPut(TraceLensRouteUrls.Thresholds, async ([FromServices] ThresholdBusiness business,
                [FromBody] ThresholdRequest request) =>
            {
                DataResponse<ThresholdListResponse> response = await business.SetOverride(request);
                return Results.Ok(response);
            })
            .WithDescription("Bir operasyon için özel eşik ekler veya günceller")
            .Produces<DataResponse<ThresholdListResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Thresholds);

            api.MapDelete(TraceLensRouteUrls.Thresholds, async ([FromServices] ThresholdBusiness business,
                [FromQuery] string? service, [FromQuery] string? operation) =>
            {
                DataResponse<ThresholdListResponse> response = await business.DeleteOverride(service, operation);
                return Results.Ok(response);
            })
            .WithDescription("Özel eşiği kaldırır; operasyon varsayılan eşiğe döner")
            .Produces<DataResponse<ThresholdListResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Thresholds);

            #endregion

            return endpoints;
        }

        private static void MapOverviewEndpoints(RouteGroupBuilder group, AppKind app)
        {
            group.MapGet(TraceLensRouteUrls.ServiceList, async ([FromServices] TraceBusiness business) =>
            {
                DataResponse<List<string>> response = await business.GetServices(app);
                return Results.Ok(response);
            })
            .WithDescription("Filtre listesi için servis/scheduler adları (son 7 gün)")
            .Produces<DataResponse<List<string>>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.Summary, async ([FromServices] TraceBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<List<OperationSummaryResponse>> response = await business.GetSummary(app, request);
                return Results.Ok(response);
            })
            .WithDescription("Operasyon bazında istek sayısı, ortalama, p95, eşiği aşan ve hatalı istek")
            .Produces<DataResponse<List<OperationSummaryResponse>>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.Totals, async ([FromServices] TraceBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<OperationSummaryResponse> response = await business.GetTotals(app, request);
                return Results.Ok(response);
            })
            .WithDescription("Filtrenin tamamı için tek satır özet (KPI kartları)")
            .Produces<DataResponse<OperationSummaryResponse>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.TimeSeries, async ([FromServices] TraceBusiness business,
                [AsParameters] TraceFilterRequest request, [FromQuery] int? bucketSeconds) =>
            {
                DataResponse<List<TimeBucketResponse>> response = await business.GetTimeSeries(app, request, bucketSeconds);
                return Results.Ok(response);
            })
            .WithDescription("Zaman grafiği: bucket başına ortalama, p95, istek, eşiği aşan, hatalı")
            .Produces<DataResponse<List<TimeBucketResponse>>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.Requests, async ([FromServices] TraceBusiness business,
                [AsParameters] TraceFilterRequest request, [FromQuery] string? sort, [FromQuery] int? limit, [FromQuery] int? offset) =>
            {
                DataResponse<PagedResponse<RequestRowResponse>> response = await business.GetRequests(app, request, sort, limit, offset);
                return Results.Ok(response);
            })
            .WithDescription("Sayfalı istek listesi (sort: time | duration)")
            .Produces<DataResponse<PagedResponse<RequestRowResponse>>>(StatusCodes.Status200OK);
        }
    }
}

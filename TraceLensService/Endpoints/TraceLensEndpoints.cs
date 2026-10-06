using CommonUtils.Interfaces;
using CommonUtils.Models;
using Microsoft.AspNetCore.Mvc;
using TraceLensService.Business;
using TraceLensService.Common;
using TraceLensService.Enums;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Alerts;
using TraceLensService.Models.Responses.Analysis;
using TraceLensService.Models.Responses.Auth;
using TraceLensService.Models.Responses.Issues;
using TraceLensService.Models.Responses.Live;
using TraceLensService.Models.Responses.Reports;
using TraceLensService.Models.Responses.Overview;
using TraceLensService.Models.Responses.ServiceDetail;
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

            #region Overview

            api.MapGet(TraceLensRouteUrls.Overview, async ([FromServices] OverviewBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<OverviewResponse> response = await business.GetOverview(request);
                return Results.Ok(response);
            })
            .WithDescription("Genel Bakış: tüm servis ve scheduler'ların özeti, kart grafikleri, açık sorun sayısı")
            .Produces<DataResponse<OverviewResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Overview);

            api.MapGet(TraceLensRouteUrls.Issues, async ([FromServices] OverviewBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<List<IssueResponse>> response = await business.GetIssues(request);
                return Results.Ok(response);
            })
            .WithDescription("Sorunlar: eşiği aşan veya hata veren endpoint/job'lar, önce alarmı olanlar (service ile filtrelenebilir)")
            .Produces<DataResponse<List<IssueResponse>>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Overview);

            api.MapGet(TraceLensRouteUrls.ServiceMap, async ([FromServices] OverviewBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<ServiceMapResponse> response = await business.GetServiceMap(request);
                return Results.Ok(response);
            })
            .WithDescription("Servis haritası: servisler, görevler, veritabanları ve aralarındaki çağrılar")
            .Produces<DataResponse<ServiceMapResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Overview);

            api.MapGet(TraceLensRouteUrls.Live, async ([FromServices] LiveBusiness business, [AsParameters] LiveRequest request) =>
            {
                DataResponse<LiveResponse> response = await business.GetLive(request);
                return Results.Ok(response);
            })
            .WithDescription("Canlı: since'ten sonra gelen istekler (en yeni üstte) ve son 60 saniyenin özeti (app: service | scheduler)")
            .Produces<DataResponse<LiveResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Overview);

            api.MapGet(TraceLensRouteUrls.Reports, async ([FromServices] ReportBusiness business, [FromQuery] string? period,
                [FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
            {
                DataResponse<ReportResponse> response = await business.GetReport(period, from, to);
                return Results.Ok(response);
            })
            .WithDescription("Rapor: bugün / dün / son 7 gün / son 30 gün / özel (period: today | yesterday | 7d | 30d | custom + from, to: yyyy-MM-dd), önceki eşit dönemle karşılaştırmalı")
            .Produces<DataResponse<ReportResponse>>(StatusCodes.Status200OK)
            .WithTags(GeneralConsts.Overview);

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

            api.MapGet(TraceLensRouteUrls.Alerts, async ([FromServices] AlertBusiness business, [AsParameters] AlertFilterRequest request) =>
            {
                DataResponse<AlertListResponse> response = await business.GetAlerts(request);
                return Results.Ok(response);
            })
            .WithDescription("Açık alarmlar ve aralıkta kapanan alarmlar; filtreler: days veya from/to, app, service, operation, kind (slow | error), minPeakMs, status (500 | 5xx)")
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

            group.MapGet(TraceLensRouteUrls.ServiceBreakdown, async ([FromServices] TraceBusiness business,
                string service, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<ServiceBreakdownResponse> response = await business.GetBreakdown(service, request);
                return Results.Ok(response);
            })
            .WithDescription("Servis Detayı: süre dağılımı (kendi kodu / dış çağrı / DB) ve metod, DB sorgusu, dış çağrı grupları")
            .Produces<DataResponse<ServiceBreakdownResponse>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.ServiceSpanSamples, async ([FromServices] TraceBusiness business,
                string service, [FromQuery] string? category, [FromQuery] string? name, [FromQuery] string? target,
                [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<List<RequestRowResponse>> response = await business.GetSpanSamples(service, category, name, target, request);
                return Results.Ok(response);
            })
            .WithDescription("Bir metod / DB sorgusu / dış çağrının en yavaş örnekleri (category: method | db | call)")
            .Produces<DataResponse<List<RequestRowResponse>>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.ServiceAnatomy, async ([FromServices] TraceBusiness business,
                string service, [FromQuery] string? operation, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<AnatomyResponse> response = await business.GetAnatomy(app, service, operation, request);
                return Results.Ok(response);
            })
            .WithDescription("İstek anatomisi: bir endpoint/job'un son istekleri ortalamada hangi adımlardan oluşuyor")
            .Produces<DataResponse<AnatomyResponse>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.Histogram, async ([FromServices] TraceBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<HistogramResponse> response = await business.GetHistogram(app, request);
                return Results.Ok(response);
            })
            .WithDescription("Süre dağılımı (logaritmik aralıklar) ve p50/p90/p99")
            .Produces<DataResponse<HistogramResponse>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.Outcomes, async ([FromServices] TraceBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<OutcomeResponse> response = await business.GetOutcomes(app, request);
                return Results.Ok(response);
            })
            .WithDescription("Durum kodu (görevlerde sonuç) dağılımı ve hata türleri")
            .Produces<DataResponse<OutcomeResponse>>(StatusCodes.Status200OK);

            group.MapGet(TraceLensRouteUrls.Instances, async ([FromServices] TraceBusiness business, [AsParameters] TraceFilterRequest request) =>
            {
                DataResponse<List<InstanceResponse>> response = await business.GetInstances(app, request);
                return Results.Ok(response);
            })
            .WithDescription("Servisin çalışan kopyaları (service.instance.id) ayrı ayrı")
            .Produces<DataResponse<List<InstanceResponse>>>(StatusCodes.Status200OK);
        }
    }
}

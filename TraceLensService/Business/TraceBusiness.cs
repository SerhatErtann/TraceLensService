using CommonUtils.Models;
using TraceLensService.Common;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.ServiceDetail;
using TraceLensService.Models.Responses.Shared;
using TraceLensService.Models.Responses.Traces;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Services/Schedulers sayfaları, Servis Detayı ve trace detayı için iş katmanı.</summary>
    public class TraceBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<TraceBusiness>>())
    {
        private readonly TraceQueries _traceQueries = serviceProvider.GetRequiredService<TraceQueries>();
        private readonly ThresholdStore _thresholds = serviceProvider.GetRequiredService<ThresholdStore>();

        #region Overview

        public Task<DataResponse<List<string>>> GetServices(AppKind app)
            => Guard(async () =>
            {
                DataResponse<List<string>> response = new();
                response.Success(await _traceQueries.GetServicesAsync(app));
                return response;
            }, GeneralConsts.ServicesNotRetrieved);

        public Task<DataResponse<List<OperationSummaryResponse>>> GetSummary(AppKind app, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<List<OperationSummaryResponse>> response = new();
                response.Success(await _traceQueries.GetSummaryAsync(request.ToFilter(app)));
                return response;
            }, GeneralConsts.SummaryNotRetrieved);

        public Task<DataResponse<OperationSummaryResponse>> GetTotals(AppKind app, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<OperationSummaryResponse> response = new();
                response.Success(await _traceQueries.GetTotalsAsync(request.ToFilter(app)));
                return response;
            }, GeneralConsts.TotalsNotRetrieved);

        public Task<DataResponse<List<TimeBucketResponse>>> GetTimeSeries(AppKind app, TraceFilterRequest request, int? bucketSeconds)
            => Guard(async () =>
            {
                DataResponse<List<TimeBucketResponse>> response = new();
                TraceFilter filter = request.ToFilter(app);
                int bucket = bucketSeconds ?? TimeBuckets.For(filter.To - filter.From);
                response.Success(await _traceQueries.GetTimeSeriesAsync(filter, bucket));
                return response;
            }, GeneralConsts.TimeSeriesNotRetrieved);

        public Task<DataResponse<PagedResponse<RequestRowResponse>>> GetRequests(
            AppKind app, TraceFilterRequest request, string? sort, int? limit, int? offset)
            => Guard(async () =>
            {
                DataResponse<PagedResponse<RequestRowResponse>> response = new();
                response.Success(await _traceQueries.GetRequestsAsync(
                    request.ToFilter(app), sort ?? "time", limit ?? DefaultRequestPageSize, offset ?? 0));
                return response;
            }, GeneralConsts.RequestsNotRetrieved);

        #endregion

        #region Servis Detayı

        public Task<DataResponse<ServiceBreakdownResponse>> GetBreakdown(string service, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<ServiceBreakdownResponse> response = new();
                if (string.IsNullOrWhiteSpace(service))
                    throw new FriendlyException(GeneralConsts.ServiceRequired);

                (DateTimeOffset from, DateTimeOffset to) = request.ResolveRange();
                Task<List<SpanGroupRow>> groupsTask = _traceQueries.GetSpanGroupsAsync(service, from, to);
                Task<List<(string Category, double SelfMs, double TotalMs, long Count)>> splitTask = _traceQueries.GetTimeSplitAsync(service, from, to);
                await Task.WhenAll(groupsTask, splitTask);

                var split = splitTask.Result.ToDictionary(s => s.Category);
                double SelfOf(params string[] categories) => categories.Sum(c => split.TryGetValue(c, out var s) ? s.SelfMs : 0);
                split.TryGetValue("root", out var root);

                // Kendi kodu = isteğin/job'un kendi süresi + servisin metodları (ikisi de çocukları hariç)
                List<(string Category, double Ms)> parts =
                [
                    ("own", SelfOf("root", SpanCategoryMethod)),
                    (SpanCategoryCall, SelfOf(SpanCategoryCall)),
                    (SpanCategoryDb, SelfOf(SpanCategoryDb)),
                    ("other", SelfOf("other"))
                ];
                double splitTotal = parts.Sum(x => x.Ms);

                ThresholdOptions thresholds = _thresholds.Current;
                List<SpanGroupResponse> groups = groupsTask.Result.Select(g =>
                {
                    double perRequest = root.Count == 0 ? 0 : (double)g.Count / root.Count;
                    return new SpanGroupResponse
                    {
                        Category = g.Category,
                        Name = g.Name,
                        Target = g.Target,
                        Count = g.Count,
                        AvgMs = g.AvgMs,
                        P95Ms = g.P95Ms,
                        MaxMs = g.MaxMs,
                        ErrorCount = g.ErrorCount,
                        TotalMs = g.TotalMs,
                        ThresholdMs = thresholds.For(service, g.Name),
                        CallsPerRequest = Math.Round(perRequest, 1),
                        Share = root.TotalMs > 0 ? Math.Round(g.TotalMs / root.TotalMs, 4) : 0,
                        SuspectedNPlusOne = g.Category == SpanCategoryDb && perRequest >= NPlusOneCallsPerRequest
                    };
                }).ToList();

                response.Success(new ServiceBreakdownResponse
                {
                    Service = service,
                    From = from.UtcDateTime,
                    To = to.UtcDateTime,
                    RequestCount = root.Count,
                    RequestTotalMs = root.TotalMs,
                    TimeSplit = parts.Select(x => new TimeSplitResponse
                    {
                        Category = x.Category,
                        TotalMs = Math.Round(x.Ms, 2),
                        Share = splitTotal > 0 ? Math.Round(x.Ms / splitTotal, 4) : 0
                    }).ToList(),
                    Methods = groups.Where(g => g.Category == SpanCategoryMethod).ToList(),
                    Database = groups.Where(g => g.Category == SpanCategoryDb).ToList(),
                    Calls = groups.Where(g => g.Category == SpanCategoryCall).ToList()
                });
                return response;
            }, GeneralConsts.BreakdownNotRetrieved);

        public Task<DataResponse<List<RequestRowResponse>>> GetSpanSamples(
            string service, string? category, string? name, string? target, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<List<RequestRowResponse>> response = new();
                if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(name))
                    throw new FriendlyException(GeneralConsts.ServiceRequired);
                if (category is not (SpanCategoryMethod or SpanCategoryDb or SpanCategoryCall))
                    throw new FriendlyException(GeneralConsts.InvalidSpanCategory);

                (DateTimeOffset from, DateTimeOffset to) = request.ResolveRange();
                response.Success(await _traceQueries.GetSpanSamplesAsync(
                    service, category, name, target ?? string.Empty, from, to, SpanSampleLimit));
                return response;
            }, GeneralConsts.SpanSamplesNotRetrieved);

        #endregion

        #region Trace

        public Task<DataResponse<TraceDetailResponse>> GetTrace(string traceId)
            => Guard(async () =>
            {
                DataResponse<TraceDetailResponse> response = new();
                List<RawSpan> spans = await _traceQueries.GetTraceSpansAsync(traceId);
                if (spans.Count == 0)
                    throw new FriendlyException(GeneralConsts.TraceNotFound);

                response.Success(TraceAnalyzer.Build(traceId, spans));
                return response;
            }, GeneralConsts.TraceNotRetrieved);

        #endregion
    }
}

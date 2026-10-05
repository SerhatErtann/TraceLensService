using CommonUtils.Models;
using TraceLensService.Common;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Analysis;
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

        #region Dağılımlar

        public Task<DataResponse<HistogramResponse>> GetHistogram(AppKind app, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<HistogramResponse> response = new();
                response.Success(await _traceQueries.GetHistogramAsync(request.ToFilter(app)));
                return response;
            }, GeneralConsts.HistogramNotRetrieved);

        public Task<DataResponse<OutcomeResponse>> GetOutcomes(AppKind app, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<OutcomeResponse> response = new();
                response.Success(await _traceQueries.GetOutcomesAsync(request.ToFilter(app)));
                return response;
            }, GeneralConsts.OutcomesNotRetrieved);

        public Task<DataResponse<List<InstanceResponse>>> GetInstances(AppKind app, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<List<InstanceResponse>> response = new();
                response.Success(await _traceQueries.GetInstancesAsync(request.ToFilter(app)));
                return response;
            }, GeneralConsts.InstancesNotRetrieved);

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

        public Task<DataResponse<AnatomyResponse>> GetAnatomy(AppKind app, string service, string? operation, TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<AnatomyResponse> response = new();
                if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(operation))
                    throw new FriendlyException(GeneralConsts.OperationRequired);

                (DateTimeOffset from, DateTimeOffset to) = request.ResolveRange();
                var (roots, spans) = await _traceQueries.GetAnatomySpansAsync(app, service, operation, from, to, AnatomySampleSize);
                response.Success(BuildAnatomy(operation, roots, spans));
                return response;
            }, GeneralConsts.AnatomyNotRetrieved);

        /// <summary>
        /// Her kök span'den aşağı inilir (servisin kendi span'leri). Dış çağrı ve DB span'inin altına inilmez: süresi tamamen
        /// o adıma yazılır. Aynı servise iç içe gelen başka bir istek (kök) ayrı istek sayılıp atlanır.
        /// </summary>
        private static AnatomyResponse BuildAnatomy(
            string operation, List<(string TraceId, string SpanId, double DurationMs)> roots, List<AnatomySpan> spans)
        {
            ILookup<(string, string?), AnatomySpan> children = spans.ToLookup(s => (s.TraceId, s.ParentSpanId));
            Dictionary<(string Category, string Name, string Target), (long Count, double TotalMs)> steps = [];
            Dictionary<string, double> split = [];

            void AddSelf(string category, double ms) => split[category] = split.GetValueOrDefault(category) + ms;
            double ChildrenMs(string traceId, string spanId) => children[(traceId, spanId)].Sum(c => c.DurationMs);

            foreach ((string traceId, string rootId, double rootMs) in roots)
            {
                AddSelf("own", Math.Max(0, rootMs - ChildrenMs(traceId, rootId)));
                Stack<AnatomySpan> stack = new(children[(traceId, rootId)]);
                while (stack.Count > 0)
                {
                    AnatomySpan span = stack.Pop();
                    if (span.Category == "root") continue;

                    var key = (span.Category, span.Name, span.Target);
                    (long count, double total) = steps.GetValueOrDefault(key);
                    steps[key] = (count + 1, total + span.DurationMs);

                    if (span.Category is SpanCategoryDb or SpanCategoryCall)
                    {
                        AddSelf(span.Category, span.DurationMs);
                        continue;
                    }
                    AddSelf(span.Category == SpanCategoryMethod ? "own" : "other", Math.Max(0, span.DurationMs - ChildrenMs(traceId, span.SpanId)));
                    foreach (AnatomySpan kid in children[(traceId, span.SpanId)])
                        stack.Push(kid);
                }
            }

            int n = roots.Count;
            double avgDuration = n == 0 ? 0 : roots.Average(r => r.DurationMs);
            double splitTotal = split.Values.Sum();
            return new AnatomyResponse
            {
                Operation = operation,
                SampleCount = n,
                AvgDurationMs = Math.Round(avgDuration, 2),
                TimeSplit = new[] { "own", SpanCategoryCall, SpanCategoryDb, "other" }.Select(c => new TimeSplitResponse
                {
                    Category = c,
                    TotalMs = Math.Round(split.GetValueOrDefault(c) / Math.Max(n, 1), 2),
                    Share = splitTotal > 0 ? Math.Round(split.GetValueOrDefault(c) / splitTotal, 4) : 0
                }).ToList(),
                Steps = steps
                    .Select(s => new AnatomyStepResponse
                    {
                        Category = s.Key.Category,
                        Name = s.Key.Name,
                        Target = s.Key.Target,
                        CallsPerRequest = Math.Round((double)s.Value.Count / n, 2),
                        MsPerRequest = Math.Round(s.Value.TotalMs / n, 2),
                        AvgMs = Math.Round(s.Value.TotalMs / s.Value.Count, 2),
                        Share = avgDuration > 0 ? Math.Round(s.Value.TotalMs / n / avgDuration, 4) : 0
                    })
                    .OrderByDescending(s => s.MsPerRequest)
                    .ToList()
            };
        }

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

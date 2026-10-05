using CommonUtils.Models;
using TraceLensService.Common;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Shared;
using TraceLensService.Models.Responses.Traces;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Services/Schedulers sayfaları ve trace detayı için iş katmanı.</summary>
    public class TraceBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<TraceBusiness>>())
    {
        private readonly TraceQueries _traceQueries = serviceProvider.GetRequiredService<TraceQueries>();

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
                int bucket = bucketSeconds ?? AutoBucket(filter.To - filter.From);
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

        /// <summary>Zaman aralığına göre grafik çözünürlüğü (saniye).</summary>
        private static int AutoBucket(TimeSpan range) => range.TotalMinutes switch
        {
            <= 15 => 15,
            <= 60 => 60,
            <= 360 => 300,
            <= 1440 => 900,
            _ => 3600
        };
    }
}

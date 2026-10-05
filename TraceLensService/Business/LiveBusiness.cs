using CommonUtils.Models;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Live;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Canlı sayfa: son gelen istekler (imleçle, sadece yeniler) ve son 60 saniyenin özeti.</summary>
    public class LiveBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<LiveBusiness>>())
    {
        private readonly TraceQueries _traceQueries = serviceProvider.GetRequiredService<TraceQueries>();

        public Task<DataResponse<LiveResponse>> GetLive(LiveRequest request)
            => Guard(async () =>
            {
                DataResponse<LiveResponse> response = new();
                AppKind? app = request.App?.ToLowerInvariant() switch
                {
                    ServiceAppType => AppKind.Service,
                    SchedulerAppType => AppKind.Scheduler,
                    _ => null
                };

                // Pencere tam saniyeye oturur ve veri gecikmesi kadar geriden biter
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset windowEnd = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() - LiveLagSeconds);
                DateTimeOffset windowStart = windowEnd.AddSeconds(-LiveWindowSeconds);

                var rowsTask = _traceQueries.GetLiveRowsAsync(app, request.Service, request.Operation,
                    request.OnlySlow ?? false, request.OnlyErrors ?? false, request.Since);
                var statsTask = _traceQueries.GetLiveStatsAsync(app, request.Service, request.Operation, windowStart, windowEnd);
                await Task.WhenAll(rowsTask, statsTask);

                List<LiveRowResponse> rows = rowsTask.Result;
                response.Success(new LiveResponse
                {
                    Items = rows,
                    Cursor = rows.Count > 0 ? rows.Max(r => r.Timestamp) : request.Since?.UtcDateTime,
                    Stats = statsTask.Result
                });
                return response;
            }, GeneralConsts.LiveNotRetrieved);
    }
}

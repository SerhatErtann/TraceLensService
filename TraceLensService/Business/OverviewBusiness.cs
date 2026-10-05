using CommonUtils.Models;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Issues;
using TraceLensService.Models.Responses.Overview;
using TraceLensService.Models.Responses.Traces;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Genel Bakış (tüm servisler) ve Sorunlar (eşiği aşan / hata veren her şey) sayfaları.</summary>
    public class OverviewBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<OverviewBusiness>>())
    {
        private readonly TraceQueries _traceQueries = serviceProvider.GetRequiredService<TraceQueries>();
        private readonly ActiveAlertCache _activeAlerts = serviceProvider.GetRequiredService<ActiveAlertCache>();
        private readonly ThresholdStore _thresholds = serviceProvider.GetRequiredService<ThresholdStore>();

        public Task<DataResponse<OverviewResponse>> GetOverview(TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<OverviewResponse> response = new();
                (DateTimeOffset from, DateTimeOffset to) = request.ResolveRange();

                Task<List<ServiceStats>> statsTask = _traceQueries.GetServiceStatsAsync(from, to);
                Task<ServiceStats> overallTask = _traceQueries.GetOverallStatsAsync(from, to);
                Task<Dictionary<(string, AppKind), List<double?>>> trendsTask = _traceQueries.GetServiceTrendsAsync(from, to, OverviewTrendBuckets);
                Task<List<IssueResponse>> issuesTask = BuildIssuesAsync(from, to, service: null);
                await Task.WhenAll(statsTask, overallTask, trendsTask, issuesTask);

                List<IssueResponse> issues = issuesTask.Result;
                double defaultThreshold = _thresholds.Current.DefaultMs;

                List<ServiceCardResponse> cards = statsTask.Result.Select(s =>
                {
                    int slowOps = issues.Count(i => i.Service == s.Service && i.App == s.App && i.IsSlow);
                    return new ServiceCardResponse
                    {
                        Service = s.Service,
                        App = s.App,
                        Status = s.ErrorRate >= IssueErrorRate ? "error" : slowOps > 0 ? "slow" : "ok",
                        Count = s.Count,
                        AvgMs = s.AvgMs,
                        P95Ms = s.P95Ms,
                        ErrorCount = s.ErrorCount,
                        ErrorRate = s.ErrorRate,
                        SlowOperationCount = slowOps,
                        ThresholdMs = defaultThreshold,
                        Trend = trendsTask.Result.TryGetValue((s.Service, s.App), out List<double?>? trend)
                            ? trend : Enumerable.Repeat<double?>(null, OverviewTrendBuckets).ToList()
                    };
                })
                // Önce hatalılar, sonra yavaşlar, sonra isme göre
                .OrderBy(c => c.Status == "error" ? 0 : c.Status == "slow" ? 1 : 2)
                .ThenBy(c => c.Service)
                .ToList();

                ServiceStats overall = overallTask.Result;
                double seconds = Math.Max(1, (to - from).TotalSeconds);
                response.Success(new OverviewResponse
                {
                    From = from.UtcDateTime,
                    To = to.UtcDateTime,
                    TrendBucketSeconds = (int)Math.Ceiling(seconds / OverviewTrendBuckets),
                    Services = cards,
                    Totals = new OverviewTotalsResponse
                    {
                        ServiceCount = cards.Count(c => c.App == AppKind.Service),
                        SchedulerCount = cards.Count(c => c.App == AppKind.Scheduler),
                        RequestCount = overall.Count,
                        RequestsPerSecond = Math.Round(overall.Count / seconds, 2),
                        AvgMs = overall.AvgMs,
                        P95Ms = overall.P95Ms,
                        ErrorCount = overall.ErrorCount,
                        ErrorRate = overall.ErrorRate,
                        OpenIssueCount = issues.Count,
                        ActiveAlertCount = _activeAlerts.Active.Count
                    }
                });
                return response;
            }, GeneralConsts.OverviewNotRetrieved);

        public Task<DataResponse<List<IssueResponse>>> GetIssues(TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<List<IssueResponse>> response = new();
                (DateTimeOffset from, DateTimeOffset to) = request.ResolveRange();
                response.Success(await BuildIssuesAsync(from, to, request.Service));
                return response;
            }, GeneralConsts.IssuesNotRetrieved);

        /// <summary>
        /// Ortalaması eşiğini aşan veya hata oranı %5'i geçen endpoint/job'lar. Sıra: açık alarmı olanlar,
        /// sonra hatalılar (orana göre), sonra yavaşlar (eşiği ne kadar aştığına göre).
        /// </summary>
        private async Task<List<IssueResponse>> BuildIssuesAsync(DateTimeOffset from, DateTimeOffset to, string? service)
        {
            Task<List<OperationSummaryResponse>> services = _traceQueries.GetSummaryAsync(new TraceFilter(AppKind.Service, from, to, service));
            Task<List<OperationSummaryResponse>> schedulers = _traceQueries.GetSummaryAsync(new TraceFilter(AppKind.Scheduler, from, to, service));
            Task<Dictionary<(string, string), string>> topErrors = _traceQueries.GetTopErrorsAsync(from, to, service);
            await Task.WhenAll(services, schedulers, topErrors);

            Dictionary<string, AlertRecord> alarms = _activeAlerts.Active.ToDictionary(a => a.Key);

            List<IssueResponse> issues = [];
            foreach ((AppKind app, List<OperationSummaryResponse> rows) in new[] { (AppKind.Service, services.Result), (AppKind.Scheduler, schedulers.Result) })
            {
                foreach (OperationSummaryResponse row in rows.Where(r => r.Count >= IssueMinRequestCount))
                {
                    bool slow = row.AvgMs > row.ThresholdMs;
                    bool errors = row.ErrorCount > 0 && row.ErrorRate >= IssueErrorRate;
                    if (!slow && !errors) continue;

                    alarms.TryGetValue($"{app}|{row.Service}|{row.Operation}", out AlertRecord? alarm);
                    issues.Add(new IssueResponse
                    {
                        App = app,
                        Service = row.Service,
                        Operation = row.Operation,
                        Kind = errors ? "error" : "slow",
                        IsSlow = slow,
                        HasErrors = errors,
                        Count = row.Count,
                        AvgMs = row.AvgMs,
                        P95Ms = row.P95Ms,
                        ThresholdMs = row.ThresholdMs,
                        ErrorCount = row.ErrorCount,
                        ErrorRate = row.ErrorRate,
                        TopError = topErrors.Result.GetValueOrDefault((row.Service, row.Operation)),
                        AlarmActive = alarm is not null,
                        AlarmSince = alarm?.FiredAt,
                        LastSeen = row.LastSeen
                    });
                }
            }

            return issues
                .OrderByDescending(i => i.AlarmActive)
                .ThenBy(i => i.Kind == "error" ? 0 : 1)
                .ThenByDescending(i => i.Kind == "error" ? i.ErrorRate : i.AvgMs / i.ThresholdMs)
                .ToList();
        }
    }
}

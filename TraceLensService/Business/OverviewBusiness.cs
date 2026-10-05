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
    /// <summary>Genel Bakış (tüm uygulamalar) ve Sorunlar (eşiği aşan / hata veren her şey) sayfaları.</summary>
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
                Task<List<TimeBucketResponse>> timelineTask = _traceQueries.GetOverallTimeSeriesAsync(from, to, TimeBuckets.For(to - from));
                Task<List<RecentErrorResponse>> recentErrorsTask = _traceQueries.GetRecentErrorsAsync(from, to, OverviewListSize);
                Task<Operations> operationsTask = LoadOperationsAsync(from, to, service: null);
                await Task.WhenAll(statsTask, overallTask, trendsTask, timelineTask, recentErrorsTask, operationsTask);

                Operations operations = operationsTask.Result;
                List<IssueResponse> issues = BuildIssues(operations);
                double defaultThreshold = _thresholds.Current.DefaultMs;

                List<ServiceCardResponse> cards = statsTask.Result.Select(s =>
                {
                    int slowOps = issues.Count(i => i.Service == s.Service && i.App == s.App && i.IsSlow);
                    // Sorunlar sayfasıyla aynı: hata oranı yüksek bir endpoint/job varsa servis toplamı düşük olsa da "hatalı"
                    bool errorOps = issues.Any(i => i.Service == s.Service && i.App == s.App && i.HasErrors);
                    List<(AppKind App, OperationSummaryResponse Row)> appOps = operations.Rows
                        .Where(o => o.App == s.App && o.Row.Service == s.Service)
                        .ToList();
                    (AppKind App, OperationSummaryResponse Row) slowest = appOps.MaxBy(o => o.Row.AvgMs);
                    return new ServiceCardResponse
                    {
                        Service = s.Service,
                        App = s.App,
                        Status = s.ErrorRate >= IssueErrorRate || errorOps ? "error" : slowOps > 0 ? "slow" : "ok",
                        Count = s.Count,
                        AvgMs = s.AvgMs,
                        P95Ms = s.P95Ms,
                        ErrorCount = s.ErrorCount,
                        ErrorRate = s.ErrorRate,
                        SlowOperationCount = slowOps,
                        SlowestOperation = slowest.Row?.Operation,
                        SlowestOperationAvgMs = slowest.Row?.AvgMs,
                        SlowestOperationThresholdMs = slowest.Row?.ThresholdMs,
                        // Kart grafiğindeki eşik çizgisi: uygulamanın operasyonları içindeki en düşük eşik
                        // (görevlerin kendi eşikleri varsa varsayılan 200 ms yanlış alarm gibi görünmesin)
                        ThresholdMs = appOps.Count > 0 ? appOps.Min(o => o.Row.ThresholdMs) : defaultThreshold,
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
                    Timeline = timelineTask.Result,
                    TimelineThresholdMs = defaultThreshold,
                    // Tek tük isteği olan operasyonlar listeyi yanıltmasın
                    SlowestOperations = operations.Rows
                        .Where(o => o.Row.Count >= IssueMinRequestCount)
                        .OrderByDescending(o => o.Row.AvgMs)
                        .Take(OverviewListSize).Select(Ranked).ToList(),
                    MostErrors = operations.Rows
                        .Where(o => o.Row.ErrorCount > 0)
                        .OrderByDescending(o => o.Row.ErrorCount).ThenByDescending(o => o.Row.ErrorRate)
                        .Take(OverviewListSize).Select(Ranked).ToList(),
                    RecentErrors = recentErrorsTask.Result,
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
                        SlowCount = overall.SlowCount,
                        SlowRate = overall.SlowRate,
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
                response.Success(BuildIssues(await LoadOperationsAsync(from, to, request.Service)));
                return response;
            }, GeneralConsts.IssuesNotRetrieved);

        /// <summary>Servis ve scheduler operasyon özetleri + operasyon başına en sık hata.</summary>
        private sealed record Operations(
            List<(AppKind App, OperationSummaryResponse Row)> Rows,
            Dictionary<(string, string), string> TopErrors);

        private async Task<Operations> LoadOperationsAsync(DateTimeOffset from, DateTimeOffset to, string? service)
        {
            Task<List<OperationSummaryResponse>> services = _traceQueries.GetSummaryAsync(new TraceFilter(AppKind.Service, from, to, service));
            Task<List<OperationSummaryResponse>> schedulers = _traceQueries.GetSummaryAsync(new TraceFilter(AppKind.Scheduler, from, to, service));
            Task<Dictionary<(string, string), string>> topErrors = _traceQueries.GetTopErrorsAsync(from, to, service);
            await Task.WhenAll(services, schedulers, topErrors);

            List<(AppKind, OperationSummaryResponse)> rows =
            [
                .. services.Result.Select(r => (AppKind.Service, r)),
                .. schedulers.Result.Select(r => (AppKind.Scheduler, r))
            ];
            return new Operations(rows, topErrors.Result);
        }

        /// <summary>
        /// Ortalaması eşiğini aşan veya hata oranı %5'i geçen endpoint/job'lar. Sıra: açık alarmı olanlar,
        /// sonra hatalılar (orana göre), sonra yavaşlar (eşiği ne kadar aştığına göre).
        /// </summary>
        private List<IssueResponse> BuildIssues(Operations operations)
        {
            Dictionary<string, AlertRecord> alarms = _activeAlerts.Active.ToDictionary(a => a.Key);

            List<IssueResponse> issues = [];
            foreach ((AppKind app, OperationSummaryResponse row) in operations.Rows.Where(o => o.Row.Count >= IssueMinRequestCount))
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
                    TopError = operations.TopErrors.GetValueOrDefault((row.Service, row.Operation)),
                    AlarmActive = alarm is not null,
                    AlarmSince = alarm?.FiredAt,
                    LastSeen = row.LastSeen
                });
            }

            return issues
                .OrderByDescending(i => i.AlarmActive)
                .ThenBy(i => i.Kind == "error" ? 0 : 1)
                .ThenByDescending(i => i.Kind == "error" ? i.ErrorRate : i.AvgMs / i.ThresholdMs)
                .ToList();
        }

        private static RankedOperationResponse Ranked((AppKind App, OperationSummaryResponse Row) o) => new()
        {
            App = o.App,
            Service = o.Row.Service,
            Operation = o.Row.Operation,
            Count = o.Row.Count,
            AvgMs = o.Row.AvgMs,
            P95Ms = o.Row.P95Ms,
            ThresholdMs = o.Row.ThresholdMs,
            ErrorCount = o.Row.ErrorCount,
            ErrorRate = o.Row.ErrorRate
        };
    }
}

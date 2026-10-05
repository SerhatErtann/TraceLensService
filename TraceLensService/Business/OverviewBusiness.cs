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
                int bucketSeconds = TimeBuckets.For(to - from);
                Task<List<TimeBucketResponse>> timelineTask = _traceQueries.GetOverallTimeSeriesAsync(from, to, bucketSeconds);
                Task<List<RecentErrorResponse>> recentErrorsTask = _traceQueries.GetRecentErrorsAsync(from, to, OverviewListSize);
                Task<Operations> operationsTask = LoadOperationsAsync(from, to, service: null);
                // Karşılaştırma: hemen önceki eşit uzunluktaki dönem
                DateTimeOffset previousFrom = from - (to - from);
                Task<ServiceStats> previousTask = _traceQueries.GetOverallStatsAsync(previousFrom, from);
                Task<List<TimeBucketResponse>> previousTimelineTask = _traceQueries.GetOverallTimeSeriesAsync(previousFrom, from, bucketSeconds);
                await Task.WhenAll(statsTask, overallTask, trendsTask, timelineTask, recentErrorsTask, operationsTask, previousTask, previousTimelineTask);

                Operations operations = operationsTask.Result;
                List<IssueResponse> issues = BuildIssues(operations);
                double defaultThreshold = _thresholds.Current.DefaultMs;

                List<ServiceCardResponse> cards = statsTask.Result.Select(s =>
                {
                    int slowOps = issues.Count(i => i.Service == s.Service && i.App == s.App && i.IsSlow);
                    List<(AppKind App, OperationSummaryResponse Row)> appOps = operations.Rows
                        .Where(o => o.App == s.App && o.Row.Service == s.Service)
                        .ToList();
                    (AppKind App, OperationSummaryResponse Row) slowest = appOps.MaxBy(o => o.Row.AvgMs);
                    return new ServiceCardResponse
                    {
                        Service = s.Service,
                        App = s.App,
                        Status = CardStatus(s, issues),
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
                ServiceStats previous = previousTask.Result;
                double seconds = Math.Max(1, (to - from).TotalSeconds);
                response.Success(new OverviewResponse
                {
                    PreviousTotals = new PeriodTotalsResponse
                    {
                        From = previousFrom.UtcDateTime,
                        To = from.UtcDateTime,
                        RequestCount = previous.Count,
                        AvgMs = previous.AvgMs,
                        P95Ms = previous.P95Ms,
                        ErrorCount = previous.ErrorCount,
                        ErrorRate = previous.ErrorRate,
                        SlowCount = previous.SlowCount,
                        SlowRate = previous.SlowRate
                    },
                    PreviousTimeline = previousTimelineTask.Result,
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

        public Task<DataResponse<ServiceMapResponse>> GetServiceMap(TraceFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<ServiceMapResponse> response = new();
                (DateTimeOffset from, DateTimeOffset to) = request.ResolveRange();

                Task<List<ServiceStats>> statsTask = _traceQueries.GetServiceStatsAsync(from, to);
                Task<Operations> operationsTask = LoadOperationsAsync(from, to, service: null);
                Task<List<ServiceEdgeRow>> httpTask = _traceQueries.GetHttpEdgesAsync(from, to);
                Task<List<ServiceEdgeRow>> dbTask = _traceQueries.GetDbEdgesAsync(from, to);
                await Task.WhenAll(statsTask, operationsTask, httpTask, dbTask);

                List<IssueResponse> issues = BuildIssues(operationsTask.Result);
                Dictionary<string, ServiceMapNodeResponse> nodes = statsTask.Result.ToDictionary(s => s.Service, s => new ServiceMapNodeResponse
                {
                    Id = s.Service,
                    Name = s.Service,
                    Kind = s.App == AppKind.Service ? "service" : "scheduler",
                    Count = s.Count,
                    AvgMs = s.AvgMs,
                    ErrorRate = s.ErrorRate,
                    Status = CardStatus(s, issues)
                });

                // Karşı tarafında span bulunmayan çağrı, aynı host:port'a giden eşleşmiş çağrılar varsa o servise sayılır
                // (ör. servis yeniden başlarken kaybolan span'ler); yoksa ayrı "dış hedef" düğümü olur.
                Dictionary<string, string> hostToService = httpTask.Result
                    .Where(e => e.Callee is not null)
                    .GroupBy(e => e.Host)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Count).First().Callee!);

                IEnumerable<(string From, string To, string Kind, string Name, ServiceEdgeRow Row)> raw =
                    httpTask.Result.Select(e =>
                    {
                        string? callee = e.Callee ?? hostToService.GetValueOrDefault(e.Host);
                        return callee is not null
                            ? (e.Caller, callee, "service", callee, e)
                            : (e.Caller, $"ext:{e.Host}", "external", e.Host, e);
                    })
                    .Concat(dbTask.Result.Select(e => (e.Caller, $"db:{e.Host}", "database", e.Host, e)));

                List<ServiceMapEdgeResponse> edges = [];
                foreach (var group in raw.GroupBy(x => (x.From, x.To)))
                {
                    long count = group.Sum(x => x.Row.Count);
                    edges.Add(new ServiceMapEdgeResponse
                    {
                        From = group.Key.From,
                        To = group.Key.To,
                        Count = count,
                        AvgMs = count == 0 ? 0 : Math.Round(group.Sum(x => x.Row.AvgMs * x.Row.Count) / count, 2),
                        // Birleşen parçaların p95'i yeniden hesaplanamaz; en büyük parçanınki gösterilir
                        P95Ms = group.MaxBy(x => x.Row.Count).Row.P95Ms,
                        ErrorCount = group.Sum(x => x.Row.ErrorCount)
                    });

                    var first = group.First();
                    if (!nodes.ContainsKey(first.To))
                        nodes[first.To] = new ServiceMapNodeResponse { Id = first.To, Name = first.Name, Kind = first.Kind };
                }

                // Veritabanı ve dış hedeflerin kendi istatistiği yok: gelen çağrılardan hesaplanır
                foreach (ServiceMapNodeResponse node in nodes.Values.Where(n => n.Kind is "database" or "external"))
                {
                    List<ServiceMapEdgeResponse> incoming = edges.Where(e => e.To == node.Id).ToList();
                    node.Count = incoming.Sum(e => e.Count);
                    node.AvgMs = node.Count == 0 ? 0 : Math.Round(incoming.Sum(e => e.AvgMs * e.Count) / node.Count, 2);
                    node.ErrorRate = node.Count == 0 ? 0 : (double)incoming.Sum(e => e.ErrorCount) / node.Count;
                    node.Status = node.ErrorRate >= IssueErrorRate ? "error" : "ok";
                }

                response.Success(new ServiceMapResponse { Nodes = [.. nodes.Values], Edges = edges });
                return response;
            }, GeneralConsts.ServiceMapNotRetrieved);

        /// <summary>
        /// Kart durumu (Genel Bakış, Servis haritası): hata oranı %5'i geçen servis ya da hata oranı yüksek bir
        /// endpoint/job'u olan → "error" (Sorunlar ile aynı); ortalaması eşiğini aşan endpoint/job'u olan → "slow".
        /// </summary>
        private static string CardStatus(ServiceStats s, List<IssueResponse> issues)
        {
            List<IssueResponse> own = issues.Where(i => i.Service == s.Service && i.App == s.App).ToList();
            if (s.ErrorRate >= IssueErrorRate || own.Any(i => i.HasErrors)) return "error";
            return own.Any(i => i.IsSlow) ? "slow" : "ok";
        }

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

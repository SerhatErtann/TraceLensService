using Microsoft.Extensions.Options;
using System.Collections;
using TraceLensService.Common;
using TraceLensService.Enums;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using TraceLensService.Models.Responses.Shared;
using TraceLensService.Models.Responses.Traces;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Contexts
{
    /// <summary>
    /// otel_traces tablosu sorguları. "Kök span" = servislerde gelen HTTP isteği (Server span),
    /// scheduler'larda job çalıştırması (job.name attribute'u olan span).
    /// </summary>
    public class TraceQueries(ClickHouseContext db, IOptionsMonitor<ThresholdOptions> thresholds)
    {
        public Task<List<string>> GetServicesAsync(AppKind app, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["appType"] = AppTypeValue(app) };
            string sql = $$"""
                SELECT DISTINCT ServiceName FROM {{TracesTable}}
                WHERE ResourceAttributes['{{AppTypeAttribute}}'] = {appType:String}
                  AND Timestamp > now() - INTERVAL 7 DAY
                ORDER BY ServiceName
                """;
            return db.QueryAsync(sql, p, r => r.GetString(0), ct);
        }

        public Task<List<OperationSummaryResponse>> GetSummaryAsync(TraceFilter filter, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string where = BuildWhere(filter, p);
            ThresholdOptions threshold = thresholds.CurrentValue;
            string thresholdSql = threshold.ToSqlNanos(p);

            string sql = $$"""
                SELECT ServiceName, SpanName,
                       count()                                AS cnt,
                       avg(Duration) / 1e6                    AS avg_ms,
                       quantile(0.95)(Duration) / 1e6         AS p95_ms,
                       max(Duration) / 1e6                    AS max_ms,
                       countIf(Duration > {{thresholdSql}})   AS slow,
                       countIf(StatusCode = '{{ErrorStatus}}') AS errors,
                       max(Timestamp)                         AS last_seen
                FROM {{TracesTable}}
                WHERE {{where}}
                GROUP BY ServiceName, SpanName
                ORDER BY avg_ms DESC
                LIMIT {{SummaryRowLimit}}
                """;

            return db.QueryAsync(sql, p, r =>
            {
                string service = r.GetString(0);
                string operation = r.GetString(1);
                return new OperationSummaryResponse
                {
                    Service = service,
                    Operation = operation,
                    Count = Convert.ToInt64(r.GetValue(2)),
                    AvgMs = Round(r.GetValue(3)),
                    P95Ms = Round(r.GetValue(4)),
                    MaxMs = Round(r.GetValue(5)),
                    SlowCount = Convert.ToInt64(r.GetValue(6)),
                    ErrorCount = Convert.ToInt64(r.GetValue(7)),
                    ThresholdMs = threshold.For(service, operation),
                    LastSeen = r.GetDateTime(8)
                };
            }, ct);
        }

        /// <summary>Filtrenin tamamı için tek satır özet. p95 operasyonlardan birleştirilemediği için ayrı hesaplanır.</summary>
        public async Task<OperationSummaryResponse> GetTotalsAsync(TraceFilter filter, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string where = BuildWhere(filter, p);
            ThresholdOptions threshold = thresholds.CurrentValue;
            string thresholdSql = threshold.ToSqlNanos(p);

            string sql = $$"""
                SELECT count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6, max(Duration) / 1e6,
                       countIf(Duration > {{thresholdSql}}), countIf(StatusCode = '{{ErrorStatus}}'), max(Timestamp)
                FROM {{TracesTable}}
                WHERE {{where}}
                """;

            List<OperationSummaryResponse> rows = await db.QueryAsync(sql, p, r =>
            {
                long count = Convert.ToInt64(r.GetValue(0));
                return new OperationSummaryResponse
                {
                    Service = filter.Service ?? string.Empty,
                    Operation = filter.Operation ?? string.Empty,
                    Count = count,
                    AvgMs = count == 0 ? 0 : Round(r.GetValue(1)),
                    P95Ms = count == 0 ? 0 : Round(r.GetValue(2)),
                    MaxMs = count == 0 ? 0 : Round(r.GetValue(3)),
                    SlowCount = Convert.ToInt64(r.GetValue(4)),
                    ErrorCount = Convert.ToInt64(r.GetValue(5)),
                    ThresholdMs = threshold.For(filter.Service ?? string.Empty, filter.Operation ?? string.Empty),
                    LastSeen = r.GetDateTime(6)
                };
            }, ct);
            return rows[0];
        }

        public Task<List<TimeBucketResponse>> GetTimeSeriesAsync(TraceFilter filter, int bucketSeconds, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["bucket"] = (uint)Math.Max(bucketSeconds, 1) };
            string where = BuildWhere(filter, p);
            string thresholdSql = thresholds.CurrentValue.ToSqlNanos(p);

            string sql = $$"""
                SELECT toStartOfInterval(Timestamp, toIntervalSecond({bucket:UInt32})) AS t,
                       count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6,
                       countIf(Duration > {{thresholdSql}}), countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}}
                WHERE {{where}}
                GROUP BY t
                ORDER BY t
                """;

            return db.QueryAsync(sql, p, r => new TimeBucketResponse
            {
                Time = r.GetDateTime(0),
                Count = Convert.ToInt64(r.GetValue(1)),
                AvgMs = Round(r.GetValue(2)),
                P95Ms = Round(r.GetValue(3)),
                SlowCount = Convert.ToInt64(r.GetValue(4)),
                ErrorCount = Convert.ToInt64(r.GetValue(5))
            }, ct);
        }

        public async Task<PagedResponse<RequestRowResponse>> GetRequestsAsync(
            TraceFilter filter, string sort, int limit, int offset, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string where = BuildWhere(filter, p);
            if (filter.OnlySlow)
                where += $" AND Duration > {thresholds.CurrentValue.ToSqlNanos(p)}";

            string orderBy = sort == "duration" ? "Duration DESC" : "Timestamp DESC";
            int safeLimit = Math.Clamp(limit, 1, MaxRequestPageSize);
            int safeOffset = Math.Max(offset, 0);
            p["limit"] = (uint)safeLimit;
            p["offset"] = (uint)safeOffset;

            string sql = $$"""
                SELECT Timestamp, TraceId, SpanId, ServiceName, SpanName, Duration / 1e6, StatusCode, StatusMessage,
                       SpanAttributes['http.response.status_code'],
                       SpanAttributes['http.request.body.size'],
                       SpanAttributes['http.response.body.size'],
                       SpanAttributes['job.status']
                FROM {{TracesTable}}
                WHERE {{where}}
                ORDER BY {{orderBy}}
                LIMIT {limit:UInt32} OFFSET {offset:UInt32}
                """;

            List<RequestRowResponse> items = await db.QueryAsync(sql, p, r => new RequestRowResponse
            {
                Timestamp = r.GetDateTime(0),
                TraceId = r.GetString(1),
                SpanId = r.GetString(2),
                Service = r.GetString(3),
                Operation = r.GetString(4),
                DurationMs = Round(r.GetValue(5)),
                Status = r.GetString(6),
                StatusMessage = NullIfEmpty(r.GetString(7)),
                HttpStatusCode = NullIfEmpty(r.GetString(8)),
                RequestBytes = ParseLong(r.GetString(9)),
                ResponseBytes = ParseLong(r.GetString(10)),
                JobStatus = NullIfEmpty(r.GetString(11))
            }, ct);

            long total = (await db.QueryAsync($"SELECT count() FROM {TracesTable} WHERE {where}", p,
                r => Convert.ToInt64(r.GetValue(0)), ct))[0];

            return new PagedResponse<RequestRowResponse> { Items = items, Total = total, Limit = safeLimit, Offset = safeOffset };
        }

        public Task<List<RawSpan>> GetTraceSpansAsync(string traceId, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["traceId"] = traceId };
            string sql = $$"""
                SELECT Timestamp, SpanId, ParentSpanId, ServiceName, SpanName, SpanKind,
                       Duration / 1e6, StatusCode, StatusMessage, SpanAttributes,
                       `Events.Timestamp`, `Events.Name`, `Events.Attributes`
                FROM {{TracesTable}}
                WHERE TraceId = {traceId:String}
                ORDER BY Timestamp
                LIMIT {{MaxSpansPerTrace}}
                """;

            return db.QueryAsync(sql, p, r => new RawSpan(
                r.GetDateTime(0), r.GetString(1), NullIfEmpty(r.GetString(2)), r.GetString(3), r.GetString(4),
                r.GetString(5), Convert.ToDouble(r.GetValue(6)), r.GetString(7), NullIfEmpty(r.GetString(8)),
                ToStringMap(r.GetValue(9)),
                ReadEvents(r.GetValue(10), r.GetValue(11), r.GetValue(12))), ct);
        }

        private static string BuildWhere(TraceFilter f, Dictionary<string, object> p)
        {
            p["fromMs"] = f.From.ToUnixTimeMilliseconds();
            p["toMs"] = f.To.ToUnixTimeMilliseconds();

            List<string> conditions =
            [
                "Timestamp >= fromUnixTimestamp64Milli({fromMs:Int64})",
                "Timestamp <= fromUnixTimestamp64Milli({toMs:Int64})",
                f.App == AppKind.Service
                    ? $"SpanKind = '{ServerSpanKind}' AND ResourceAttributes['{AppTypeAttribute}'] = '{ServiceAppType}'"
                    : $"SpanAttributes['{JobNameAttribute}'] != ''"
            ];

            if (!string.IsNullOrWhiteSpace(f.Service))
            {
                p["service"] = f.Service;
                conditions.Add("ServiceName = {service:String}");
            }
            if (!string.IsNullOrWhiteSpace(f.Operation))
            {
                p["operation"] = f.Operation;
                conditions.Add("SpanName = {operation:String}");
            }
            if (f.MinDurationMs is double min)
            {
                p["minNs"] = ThresholdOptions.ToNanos(min);
                conditions.Add("Duration >= {minNs:UInt64}");
            }
            if (f.OnlyErrors)
                conditions.Add($"StatusCode = '{ErrorStatus}'");

            return string.Join(" AND ", conditions);
        }

        private static string AppTypeValue(AppKind app) => app == AppKind.Service ? ServiceAppType : SchedulerAppType;

        private static double Round(object value) => Math.Round(Convert.ToDouble(value), 2);

        private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        private static long? ParseLong(string value) => long.TryParse(value, out long v) ? v : null;

        private static Dictionary<string, string> ToStringMap(object? value)
        {
            Dictionary<string, string> result = [];
            if (value is IDictionary dict)
                foreach (DictionaryEntry e in dict)
                    result[e.Key.ToString()!] = e.Value?.ToString() ?? string.Empty;
            return result;
        }

        private static List<SpanEventResponse> ReadEvents(object timestamps, object names, object attributes)
        {
            List<SpanEventResponse> events = [];
            if (timestamps is not IList ts || names is not IList ns || attributes is not IList attrs)
                return events;

            for (int i = 0; i < ns.Count; i++)
                events.Add(new SpanEventResponse
                {
                    Timestamp = Convert.ToDateTime(ts[i]),
                    Name = ns[i]?.ToString() ?? string.Empty,
                    Attributes = ToStringMap(attrs[i])
                });
            return events;
        }
    }
}

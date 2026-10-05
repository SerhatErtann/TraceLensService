using System.Collections;
using TraceLensService.Common;
using TraceLensService.Enums;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using TraceLensService.Models.Responses.Analysis;
using TraceLensService.Models.Responses.Live;
using TraceLensService.Models.Responses.Overview;
using TraceLensService.Models.Responses.Shared;
using TraceLensService.Models.Responses.Traces;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Contexts
{
    /// <summary>
    /// otel_traces tablosu sorguları. "Kök span" = servislerde gelen HTTP isteği (Server span),
    /// scheduler'larda job çalıştırması (job.name attribute'u olan span).
    /// </summary>
    public class TraceQueries(ClickHouseContext db, ThresholdStore thresholds)
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
            ThresholdOptions threshold = thresholds.Current;
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
            ThresholdOptions threshold = thresholds.Current;
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
            string thresholdSql = thresholds.Current.ToSqlNanos(p);

            string sql = $$"""
                SELECT toStartOfInterval(Timestamp, toIntervalSecond({bucket:UInt32})) AS t, {{TimeBucketColumns(thresholdSql)}}
                FROM {{TracesTable}}
                WHERE {{where}}
                GROUP BY t
                ORDER BY t
                """;

            return db.QueryAsync(sql, p, MapTimeBucket, ct);
        }

        // Zaman grafiği bucket'ı: istek, ortalama, p50/p90/p95/p99, eşiği aşan, hatalı (t sütunundan sonra gelir)
        private static string TimeBucketColumns(string thresholdSql) => $$"""
            count(), avg(Duration) / 1e6, quantiles(0.5, 0.9, 0.95, 0.99)(Duration) AS q,
            q[1] / 1e6, q[2] / 1e6, q[3] / 1e6, q[4] / 1e6,
            countIf(Duration > {{thresholdSql}}), countIf(StatusCode = '{{ErrorStatus}}')
            """;

        private static TimeBucketResponse MapTimeBucket(System.Data.Common.DbDataReader r) => new()
        {
            Time = r.GetDateTime(0),
            Count = Convert.ToInt64(r.GetValue(1)),
            AvgMs = Round(r.GetValue(2)),
            P50Ms = Round(r.GetValue(4)),
            P90Ms = Round(r.GetValue(5)),
            P95Ms = Round(r.GetValue(6)),
            P99Ms = Round(r.GetValue(7)),
            SlowCount = Convert.ToInt64(r.GetValue(8)),
            ErrorCount = Convert.ToInt64(r.GetValue(9))
        };

        public async Task<PagedResponse<RequestRowResponse>> GetRequestsAsync(
            TraceFilter filter, string sort, int limit, int offset, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string where = BuildWhere(filter, p);
            if (filter.OnlySlow)
                where += $" AND Duration > {thresholds.Current.ToSqlNanos(p)}";

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

        #region Dağılımlar (Servisler/Görevler ve Servis Detayı)

        /// <summary>Süre dağılımı: istekler HistogramEdgesMs aralıklarına dağıtılır, ayrıca p50/p90/p99.</summary>
        public async Task<HistogramResponse> GetHistogramAsync(TraceFilter filter, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["edges"] = HistogramEdgesMs.Select(ThresholdOptions.ToNanos).ToArray() };
            string where = BuildWhere(filter, p);
            AppendOnlySlow(filter, p, ref where);

            // arrayFirstIndex: 1 = ilk sınırdan kısa, 0 = son sınırdan uzun (son aralık)
            string sql = $$"""
                SELECT arrayFirstIndex(e -> Duration < e, {edges:Array(UInt64)}) AS b, count(), countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}}
                WHERE {{where}}
                GROUP BY b
                """;
            string summarySql = $$"""
                SELECT count(), quantiles(0.5, 0.9, 0.99)(Duration) AS q, q[1] / 1e6, q[2] / 1e6, q[3] / 1e6
                FROM {{TracesTable}}
                WHERE {{where}}
                """;
            Task<List<(int Index, long Count, long Errors)>> bucketsTask = db.QueryAsync(sql, p, r =>
                (Convert.ToInt32(r.GetValue(0)), Convert.ToInt64(r.GetValue(1)), Convert.ToInt64(r.GetValue(2))), ct);
            Task<List<HistogramResponse>> summaryTask = db.QueryAsync(summarySql, p, r =>
            {
                long count = Convert.ToInt64(r.GetValue(0));
                return new HistogramResponse
                {
                    Count = count,
                    P50Ms = count == 0 ? 0 : Round(r.GetValue(2)),
                    P90Ms = count == 0 ? 0 : Round(r.GetValue(3)),
                    P99Ms = count == 0 ? 0 : Round(r.GetValue(4))
                };
            }, ct);
            await Task.WhenAll(bucketsTask, summaryTask);

            HistogramResponse result = summaryTask.Result[0];
            // Aralık sırası: 1..N sınırlar, 0 = sonuncudan uzun → N+1. İlk ve son dolu aralık arası boşluksuz döner.
            Dictionary<int, (long Count, long Errors)> byIndex = bucketsTask.Result
                .ToDictionary(b => b.Index == 0 ? HistogramEdgesMs.Length + 1 : b.Index, b => (b.Count, b.Errors));
            if (byIndex.Count == 0) return result;

            for (int i = byIndex.Keys.Min(); i <= byIndex.Keys.Max(); i++)
            {
                byIndex.TryGetValue(i, out (long Count, long Errors) b);
                result.Buckets.Add(new HistogramBucketResponse
                {
                    FromMs = i == 1 ? 0 : HistogramEdgesMs[i - 2],
                    ToMs = i <= HistogramEdgesMs.Length ? HistogramEdgesMs[i - 1] : null,
                    Count = b.Count,
                    ErrorCount = b.Errors
                });
            }
            return result;
        }

        /// <summary>Durum kodu (görevlerde sonuç) dağılımı ve hata türleri.</summary>
        public async Task<OutcomeResponse> GetOutcomesAsync(TraceFilter filter, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["limit"] = (uint)OutcomeErrorTypeLimit };
            string where = BuildWhere(filter, p);
            AppendOnlySlow(filter, p, ref where);

            string statusExpr = filter.App == AppKind.Service
                ? "SpanAttributes['http.response.status_code']"
                : $"if(SpanAttributes['job.status'] != '', SpanAttributes['job.status'], if(StatusCode = '{ErrorStatus}', 'failed', 'succeeded'))";
            string statusSql = $$"""
                SELECT {{statusExpr}} AS s, count(), countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}}
                WHERE {{where}}
                GROUP BY s
                ORDER BY count() DESC
                """;

            // Hata türü: exception tipi (error.type ya da exception event'i), yoksa HTTP kodu, yoksa mesaj
            string errorTypesSql = $$"""
                SELECT multiIf(SpanAttributes['error.type'] != '' AND NOT match(SpanAttributes['error.type'], '^[0-9]+$'), SpanAttributes['error.type'],
                               ex_type != '', ex_type,
                               SpanAttributes['http.response.status_code'] != '', concat('HTTP ', SpanAttributes['http.response.status_code']),
                               StatusMessage != '', StatusMessage,
                               'Bilinmeyen hata') AS type,
                       count(),
                       anyIf(if(StatusMessage != '', StatusMessage, ex_message), if(StatusMessage != '', StatusMessage, ex_message) != ''),
                       topK(1)(SpanName)[1],
                       argMax(TraceId, Timestamp),
                       max(Timestamp)
                FROM (
                    SELECT *, arrayFirst(a -> a['exception.type'] != '', `Events.Attributes`) AS ex,
                           ex['exception.type'] AS ex_type, ex['exception.message'] AS ex_message
                    FROM {{TracesTable}}
                    WHERE {{where}} AND StatusCode = '{{ErrorStatus}}'
                )
                GROUP BY type
                ORDER BY count() DESC
                LIMIT {limit:UInt32}
                """;

            Task<List<StatusCountResponse>> statusesTask = db.QueryAsync(statusSql, p, r => new StatusCountResponse
            {
                Status = r.GetString(0),
                Count = Convert.ToInt64(r.GetValue(1)),
                ErrorCount = Convert.ToInt64(r.GetValue(2))
            }, ct);
            Task<List<ErrorTypeResponse>> typesTask = db.QueryAsync(errorTypesSql, p, r => new ErrorTypeResponse
            {
                Type = r.GetString(0),
                Count = Convert.ToInt64(r.GetValue(1)),
                ExampleMessage = NullIfEmpty(r.GetString(2)),
                TopOperation = r.GetString(3),
                LastTraceId = r.GetString(4),
                LastSeen = r.GetDateTime(5)
            }, ct);
            await Task.WhenAll(statusesTask, typesTask);

            return new OutcomeResponse
            {
                Count = statusesTask.Result.Sum(s => s.Count),
                Statuses = statusesTask.Result,
                ErrorTypes = typesTask.Result
            };
        }

        /// <summary>Servisin çalışan kopyaları (service.instance.id) ayrı ayrı: biri diğerlerinden yavaş/hatalı mı?</summary>
        public Task<List<InstanceResponse>> GetInstancesAsync(TraceFilter filter, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string where = BuildWhere(filter, p);
            string thresholdSql = thresholds.Current.ToSqlNanos(p);
            string sql = $$"""
                SELECT ResourceAttributes['service.instance.id'] AS inst, any(ResourceAttributes['host.name']),
                       count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6,
                       countIf(Duration > {{thresholdSql}}), countIf(StatusCode = '{{ErrorStatus}}'), min(Timestamp), max(Timestamp)
                FROM {{TracesTable}}
                WHERE {{where}}
                GROUP BY inst
                ORDER BY max(Timestamp) DESC
                LIMIT {{InstanceLimit}}
                """;
            return db.QueryAsync(sql, p, r => new InstanceResponse
            {
                InstanceId = r.GetString(0),
                Host = NullIfEmpty(r.GetString(1)),
                Count = Convert.ToInt64(r.GetValue(2)),
                AvgMs = Round(r.GetValue(3)),
                P95Ms = Round(r.GetValue(4)),
                SlowCount = Convert.ToInt64(r.GetValue(5)),
                ErrorCount = Convert.ToInt64(r.GetValue(6)),
                FirstSeen = r.GetDateTime(7),
                LastSeen = r.GetDateTime(8)
            }, ct);
        }

        // İstek listesindeki "Sadece eşiği aşanlar" ile aynı koşul
        private void AppendOnlySlow(TraceFilter filter, Dictionary<string, object> p, ref string where)
        {
            if (filter.OnlySlow)
                where += $" AND Duration > {thresholds.Current.ToSqlNanos(p)}";
        }

        #endregion

        #region Genel Bakış / Sorunlar (servis ve scheduler birlikte)

        // Her iki sayfanın kök span'leri: servislerde gelen HTTP isteği, scheduler'larda job çalıştırması
        internal const string AnyRootCondition =
            $"((SpanKind = '{ServerSpanKind}' AND ResourceAttributes['{AppTypeAttribute}'] = '{ServiceAppType}') OR SpanAttributes['{JobNameAttribute}'] != '')";
        internal const string AppKindExpr = $"if(SpanAttributes['{JobNameAttribute}'] != '', 'Scheduler', 'Service')";

        /// <summary>Servis/scheduler başına istek sayısı, ortalama, p95 ve hata sayısı.</summary>
        public Task<List<ServiceStats>> GetServiceStatsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string sql = $$"""
                SELECT ServiceName, {{AppKindExpr}} AS app,
                       count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6, countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}}
                WHERE {{RangeCondition(from, to, p)}} AND {{AnyRootCondition}}
                GROUP BY ServiceName, app
                ORDER BY ServiceName
                """;
            return db.QueryAsync(sql, p, r => new ServiceStats(
                r.GetString(0), Enum.Parse<AppKind>(r.GetString(1)),
                Convert.ToInt64(r.GetValue(2)), Round(r.GetValue(3)), Round(r.GetValue(4)), Convert.ToInt64(r.GetValue(5))), ct);
        }

        /// <summary>Tüm servisler için tek satır: istek, ortalama, p95, hata.</summary>
        public async Task<ServiceStats> GetOverallStatsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string thresholdSql = thresholds.Current.ToSqlNanos(p);
            string sql = $$"""
                SELECT count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6, countIf(StatusCode = '{{ErrorStatus}}'),
                       countIf(Duration > {{thresholdSql}})
                FROM {{TracesTable}}
                WHERE {{RangeCondition(from, to, p)}} AND {{AnyRootCondition}}
                """;
            return (await db.QueryAsync(sql, p, r =>
            {
                long count = Convert.ToInt64(r.GetValue(0));
                return new ServiceStats(string.Empty, AppKind.Service, count,
                    count == 0 ? 0 : Round(r.GetValue(1)), count == 0 ? 0 : Round(r.GetValue(2)), Convert.ToInt64(r.GetValue(3)),
                    Convert.ToInt64(r.GetValue(4)));
            }, ct))[0];
        }

        /// <summary>
        /// Kart grafikleri: aralığı <paramref name="bucketCount"/> eşit parçaya bölüp servis başına ortalama süre.
        /// Veri olmayan parça null kalır (grafikte boşluk).
        /// </summary>
        public async Task<Dictionary<(string Service, AppKind App), List<double?>>> GetServiceTrendsAsync(
            DateTimeOffset from, DateTimeOffset to, int bucketCount, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new()
            {
                ["fromSec"] = from.ToUnixTimeSeconds(),
                ["bucketSec"] = (long)Math.Max(1, Math.Ceiling((to - from).TotalSeconds / bucketCount))
            };
            string sql = $$"""
                SELECT ServiceName, {{AppKindExpr}} AS app,
                       intDiv(toUnixTimestamp(Timestamp) - {fromSec:Int64}, {bucketSec:Int64}) AS b,
                       avg(Duration) / 1e6
                FROM {{TracesTable}}
                WHERE {{RangeCondition(from, to, p)}} AND {{AnyRootCondition}}
                GROUP BY ServiceName, app, b
                """;

            Dictionary<(string, AppKind), List<double?>> result = [];
            List<(string Service, AppKind App, long Bucket, double Avg)> rows = await db.QueryAsync(sql, p, r =>
                (r.GetString(0), Enum.Parse<AppKind>(r.GetString(1)), Convert.ToInt64(r.GetValue(2)), Round(r.GetValue(3))), ct);
            foreach (var row in rows)
            {
                if (!result.TryGetValue((row.Service, row.App), out List<double?>? series))
                    result[(row.Service, row.App)] = series = Enumerable.Repeat<double?>(null, bucketCount).ToList();
                if (row.Bucket >= 0 && row.Bucket < bucketCount)
                    series[(int)row.Bucket] = row.Avg;
            }
            return result;
        }

        /// <summary>Tüm uygulamaların zaman grafiği (ortalama, p95, istek, eşiği aşan, hatalı).</summary>
        public Task<List<TimeBucketResponse>> GetOverallTimeSeriesAsync(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["bucket"] = (uint)Math.Max(bucketSeconds, 1) };
            string range = RangeCondition(from, to, p);
            string thresholdSql = thresholds.Current.ToSqlNanos(p);
            string sql = $$"""
                SELECT toStartOfInterval(Timestamp, toIntervalSecond({bucket:UInt32})) AS t, {{TimeBucketColumns(thresholdSql)}}
                FROM {{TracesTable}}
                WHERE {{range}} AND {{AnyRootCondition}}
                GROUP BY t
                ORDER BY t
                """;
            return db.QueryAsync(sql, p, MapTimeBucket, ct);
        }

        /// <summary>
        /// Servisler arası HTTP çağrıları: çağıran servis → çağrılan servis (karşı tarafın Server span'i) ya da span'i
        /// yoksa host:port. Instrumented aynı host'a giden eşleşmeyen çağrılar iş katmanında o servise katılır.
        /// </summary>
        public Task<List<ServiceEdgeRow>> GetHttpEdgesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["marginMs"] = (long)CalleeMatchMarginMs };
            string range = RangeCondition(from, to, p);
            string sql = $$"""
                SELECT c.ServiceName, s.callee, c.host, count(), avg(c.Duration) / 1e6, quantile(0.95)(c.Duration) / 1e6,
                       countIf(c.StatusCode = '{{ErrorStatus}}')
                FROM (
                    SELECT TraceId, SpanId, ServiceName, Duration, StatusCode,
                           if(SpanAttributes['server.port'] = '', SpanAttributes['server.address'],
                              concat(SpanAttributes['server.address'], ':', SpanAttributes['server.port'])) AS host
                    FROM {{TracesTable}}
                    WHERE {{range}} AND SpanKind = 'Client' AND SpanAttributes['http.request.method'] != ''
                      AND NOT (mapContains(SpanAttributes, 'db.system') OR mapContains(SpanAttributes, 'db.system.name'))
                ) AS c
                LEFT JOIN (
                    SELECT TraceId, ParentSpanId, ServiceName AS callee
                    FROM {{TracesTable}}
                    WHERE SpanKind = '{{ServerSpanKind}}'
                      AND Timestamp >= fromUnixTimestamp64Milli({fromMs:Int64} - {marginMs:Int64})
                      AND Timestamp <= fromUnixTimestamp64Milli({toMs:Int64} + {marginMs:Int64})
                ) AS s ON s.TraceId = c.TraceId AND s.ParentSpanId = c.SpanId
                GROUP BY c.ServiceName, s.callee, c.host
                """;
            return db.QueryAsync(sql, p, r => new ServiceEdgeRow(
                r.GetString(0), NullIfEmpty(r.GetString(1)), r.GetString(2), Convert.ToInt64(r.GetValue(3)),
                Round(r.GetValue(4)), Round(r.GetValue(5)), Convert.ToInt64(r.GetValue(6))), ct);
        }

        /// <summary>Servis → veritabanı (db.system · db.name) çağrıları.</summary>
        public Task<List<ServiceEdgeRow>> GetDbEdgesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string sql = $$"""
                SELECT ServiceName,
                       concat(if(SpanAttributes['db.system.name'] != '', SpanAttributes['db.system.name'], SpanAttributes['db.system']),
                              if(SpanAttributes['db.namespace'] != '', concat(' · ', SpanAttributes['db.namespace']),
                                 if(SpanAttributes['db.name'] != '', concat(' · ', SpanAttributes['db.name']), ''))) AS target,
                       count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6, countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}}
                WHERE {{RangeCondition(from, to, p)}}
                  AND (mapContains(SpanAttributes, 'db.system') OR mapContains(SpanAttributes, 'db.system.name'))
                GROUP BY ServiceName, target
                """;
            return db.QueryAsync(sql, p, r => new ServiceEdgeRow(
                r.GetString(0), null, r.GetString(1), Convert.ToInt64(r.GetValue(2)),
                Round(r.GetValue(3)), Round(r.GetValue(4)), Convert.ToInt64(r.GetValue(5))), ct);
        }

        /// <summary>En son hatalı istekler/çalışmalar (servis ve scheduler birlikte).</summary>
        public Task<List<RecentErrorResponse>> GetRecentErrorsAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["limit"] = (uint)Math.Clamp(limit, 1, 50) };
            string sql = $$"""
                SELECT Timestamp, TraceId, ServiceName, SpanName, Duration / 1e6, {{AppKindExpr}},
                       if(StatusMessage != '', StatusMessage,
                          if(SpanAttributes['http.response.status_code'] != '', concat('HTTP ', SpanAttributes['http.response.status_code']), 'Hata'))
                FROM {{TracesTable}}
                WHERE {{RangeCondition(from, to, p)}} AND {{AnyRootCondition}} AND StatusCode = '{{ErrorStatus}}'
                ORDER BY Timestamp DESC
                LIMIT {limit:UInt32}
                """;
            return db.QueryAsync(sql, p, r => new RecentErrorResponse
            {
                Timestamp = r.GetDateTime(0),
                TraceId = r.GetString(1),
                Service = r.GetString(2),
                Operation = r.GetString(3),
                DurationMs = Round(r.GetValue(4)),
                App = Enum.Parse<AppKind>(r.GetString(5)),
                Error = r.GetString(6)
            }, ct);
        }

        /// <summary>Hatalı istekleri olan operasyonlar için en sık hata (exception mesajı ya da HTTP kodu).</summary>
        public async Task<Dictionary<(string Service, string Operation), string>> GetTopErrorsAsync(
            DateTimeOffset from, DateTimeOffset to, string? service, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string serviceCondition = string.Empty;
            if (!string.IsNullOrWhiteSpace(service))
            {
                p["service"] = service;
                serviceCondition = " AND ServiceName = {service:String}";
            }
            string sql = $$"""
                SELECT ServiceName, SpanName,
                       topK(1)(if(StatusMessage != '', StatusMessage,
                                  if(SpanAttributes['http.response.status_code'] != '', concat('HTTP ', SpanAttributes['http.response.status_code']), '')))
                FROM {{TracesTable}}
                WHERE {{RangeCondition(from, to, p)}} AND {{AnyRootCondition}} AND StatusCode = '{{ErrorStatus}}'{{serviceCondition}}
                GROUP BY ServiceName, SpanName
                """;

            Dictionary<(string, string), string> result = [];
            foreach ((string svc, string op, string? error) in await db.QueryAsync(sql, p, r =>
                (r.GetString(0), r.GetString(1), (r.GetValue(2) as IList)?.Cast<object>().FirstOrDefault()?.ToString()), ct))
            {
                if (!string.IsNullOrEmpty(error))
                    result[(svc, op)] = error;
            }
            return result;
        }

        #endregion

        #region Canlı

        /// <summary>Canlı sayfanın ortak koşulu: kök span'ler (uygulama türüne göre), servis ve operasyon.</summary>
        private static string LiveCondition(AppKind? app, string? service, string? operation, Dictionary<string, object> p)
        {
            List<string> conditions =
            [
                app switch
                {
                    AppKind.Service => $"SpanKind = '{ServerSpanKind}' AND ResourceAttributes['{AppTypeAttribute}'] = '{ServiceAppType}'",
                    AppKind.Scheduler => $"SpanAttributes['{JobNameAttribute}'] != ''",
                    _ => AnyRootCondition
                }
            ];
            if (!string.IsNullOrWhiteSpace(service))
            {
                p["service"] = service;
                conditions.Add("ServiceName = {service:String}");
            }
            if (!string.IsNullOrWhiteSpace(operation))
            {
                p["operation"] = operation;
                conditions.Add("SpanName = {operation:String}");
            }
            return string.Join(" AND ", conditions);
        }

        /// <summary>
        /// Son gelen istekler, en yeni üstte. <paramref name="since"/> verilirse ondan LiveLookbackSeconds geriden başlar
        /// (geç yazılan span'ler için); tekrarları dashboard SpanId ile ayıklar.
        /// </summary>
        public Task<List<LiveRowResponse>> GetLiveRowsAsync(AppKind? app, string? service, string? operation,
            bool onlySlow, bool onlyErrors, DateTimeOffset? since, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["limit"] = (uint)(since is null ? LiveInitialRows : LiveMaxRows) };
            string where = LiveCondition(app, service, operation, p);
            if (since is DateTimeOffset s)
            {
                p["sinceMs"] = s.AddSeconds(-LiveLookbackSeconds).ToUnixTimeMilliseconds();
                where += " AND Timestamp > fromUnixTimestamp64Milli({sinceMs:Int64})";
            }
            else
            {
                where += " AND Timestamp > now64() - INTERVAL 5 MINUTE";
            }
            if (onlySlow) where += $" AND Duration > {thresholds.Current.ToSqlNanos(p)}";
            if (onlyErrors) where += $" AND StatusCode = '{ErrorStatus}'";

            string sql = $$"""
                SELECT Timestamp, TraceId, SpanId, ServiceName, SpanName, Duration / 1e6, StatusCode, StatusMessage,
                       SpanAttributes['http.response.status_code'], SpanAttributes['job.status'], {{AppKindExpr}}
                FROM {{TracesTable}}
                WHERE {{where}}
                ORDER BY Timestamp DESC
                LIMIT {limit:UInt32}
                """;
            return db.QueryAsync(sql, p, r => new LiveRowResponse
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
                JobStatus = NullIfEmpty(r.GetString(9)),
                App = Enum.Parse<AppKind>(r.GetString(10))
            }, ct);
        }

        /// <summary>[windowStart, windowEnd) aralığının özeti ve saniye başına istek / eşiği aşan / hatalı.</summary>
        public async Task<LiveStatsResponse> GetLiveStatsAsync(AppKind? app, string? service, string? operation,
            DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new()
            {
                ["fromMs"] = windowStart.ToUnixTimeMilliseconds(),
                ["toMs"] = windowEnd.ToUnixTimeMilliseconds()
            };
            string where = LiveCondition(app, service, operation, p)
                + " AND Timestamp >= fromUnixTimestamp64Milli({fromMs:Int64}) AND Timestamp < fromUnixTimestamp64Milli({toMs:Int64})";
            string thresholdSql = thresholds.Current.ToSqlNanos(p);

            string summarySql = $$"""
                SELECT count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6,
                       countIf(Duration > {{thresholdSql}}), countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}} WHERE {{where}}
                """;
            string secondsSql = $$"""
                SELECT toUnixTimestamp(toStartOfSecond(Timestamp)) AS s, count(),
                       countIf(Duration > {{thresholdSql}}), countIf(StatusCode = '{{ErrorStatus}}')
                FROM {{TracesTable}} WHERE {{where}}
                GROUP BY s
                """;
            string topErrorSql = $$"""
                SELECT ServiceName, count() AS c FROM {{TracesTable}}
                WHERE {{where}} AND StatusCode = '{{ErrorStatus}}'
                GROUP BY ServiceName ORDER BY c DESC LIMIT 1
                """;

            var summaryTask = db.QueryAsync(summarySql, p, r => (Count: Convert.ToInt64(r.GetValue(0)), Avg: r.GetValue(1), P95: r.GetValue(2),
                Slow: Convert.ToInt64(r.GetValue(3)), Errors: Convert.ToInt64(r.GetValue(4))), ct);
            var secondsTask = db.QueryAsync(secondsSql, p, r => (Second: Convert.ToInt64(r.GetValue(0)), Count: Convert.ToInt64(r.GetValue(1)),
                Slow: Convert.ToInt64(r.GetValue(2)), Errors: Convert.ToInt64(r.GetValue(3))), ct);
            var topErrorTask = db.QueryAsync(topErrorSql, p, r => (Service: r.GetString(0), Count: Convert.ToInt64(r.GetValue(1))), ct);
            await Task.WhenAll(summaryTask, secondsTask, topErrorTask);

            var summary = summaryTask.Result[0];
            var bySecond = secondsTask.Result.ToDictionary(s => s.Second);
            long startSec = windowStart.ToUnixTimeSeconds();
            int windowSeconds = (int)(windowEnd - windowStart).TotalSeconds;
            List<LiveSecondResponse> seconds = Enumerable.Range(0, windowSeconds).Select(i =>
            {
                bySecond.TryGetValue(startSec + i, out var s);
                return new LiveSecondResponse
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(startSec + i).UtcDateTime,
                    Count = s.Count,
                    SlowCount = s.Slow,
                    ErrorCount = s.Errors
                };
            }).ToList();

            var topError = topErrorTask.Result.FirstOrDefault();
            return new LiveStatsResponse
            {
                WindowSeconds = windowSeconds,
                LagSeconds = LiveLagSeconds,
                WindowEnd = windowEnd.UtcDateTime,
                Count = summary.Count,
                RequestsPerSecond = Math.Round(seconds.TakeLast(LiveRateSeconds).Average(s => (double)s.Count), 1),
                AvgMs = summary.Count == 0 ? 0 : Round(summary.Avg),
                P95Ms = summary.Count == 0 ? 0 : Round(summary.P95),
                SlowCount = summary.Slow,
                ErrorCount = summary.Errors,
                TopErrorService = topError.Service,
                TopErrorServiceCount = topError.Count,
                Seconds = seconds
            };
        }

        #endregion

        #region Servis Detayı (bir servisin içi: metodlar, DB sorguları, dış çağrılar)

        // Bir span'in türü. Kök = servise gelen istek veya job çalışması; "other" pastada "Diğer" olur.
        private const string SpanCategoryExpr = $$"""
            multiIf(mapContains(SpanAttributes, 'db.system') OR mapContains(SpanAttributes, 'db.system.name'), '{{SpanCategoryDb}}',
                    SpanKind = 'Client' AND SpanAttributes['http.request.method'] != '', '{{SpanCategoryCall}}',
                    SpanAttributes['{{JobNameAttribute}}'] != '' OR SpanKind = '{{ServerSpanKind}}', 'root',
                    SpanKind = 'Internal', '{{SpanCategoryMethod}}', 'other')
            """;

        /// <summary>
        /// Servisin metod / DB / dış çağrı span'leri, okunur adlarıyla (SpanNamer ile aynı kurallar):
        /// DB span'i "SELECT Orders", dış çağrı çağrılan servisin route'u (karşı taraf enstrümante değilse
        /// sayılar {id} yapılmış URL yolu). ClickHouse regex'i \b desteklemediği için kelime sınırı [^\w] ile yazılır.
        /// </summary>
        /// <param name="traceIds">Verilirse sadece bu trace'ler ve tüm türler (kök dahil) döner; istek anatomisi için.</param>
        private static string ServiceSpansSource(string service, DateTimeOffset from, DateTimeOffset to, Dictionary<string, object> p,
            string[]? traceIds = null)
        {
            p["service"] = service;
            p["marginMs"] = (long)CalleeMatchMarginMs;
            string range = RangeCondition(from, to, p);
            string spanFilter = $"cat IN ('{SpanCategoryMethod}', '{SpanCategoryDb}', '{SpanCategoryCall}')";
            if (traceIds is not null)
            {
                p["traceIds"] = traceIds;
                // Kök span aralığın başında başlayıp çocukları sonra bitebilir; pay bırakılır
                range = "Timestamp >= fromUnixTimestamp64Milli({fromMs:Int64} - {marginMs:Int64}) AND Timestamp <= fromUnixTimestamp64Milli({toMs:Int64} + {marginMs:Int64}) AND TraceId IN {traceIds:Array(String)}";
                spanFilter = "1";
            }
            return $$"""
                (
                    SELECT c.TraceId AS TraceId, c.SpanId AS SpanId, ParentSpanId, Timestamp, Duration, StatusCode, StatusMessage, http_code, cat,
                           multiIf(cat = '{{SpanCategoryDb}}', if(verb = '', SpanName, if(tbl = '', verb, concat(verb, ' ', tbl))),
                                   cat = '{{SpanCategoryCall}}', if(callee_op != '', callee_op, url_name),
                                   SpanName) AS op_name,
                           if(cat = '{{SpanCategoryCall}}', if(callee_service != '', callee_service, host), '') AS target
                    FROM (
                        SELECT TraceId, SpanId, ParentSpanId, Timestamp, Duration, StatusCode, StatusMessage, SpanName,
                               SpanAttributes['http.response.status_code'] AS http_code,
                               {{SpanCategoryExpr}} AS cat,
                               if(SpanAttributes['db.query.text'] != '', SpanAttributes['db.query.text'], SpanAttributes['db.statement']) AS stmt,
                               upper(extract(stmt, '(?i)(?:^|[^\\w])(SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC)(?:[^\\w]|$)')) AS verb,
                               extract(stmt, '(?i)(?:^|[^\\w])(?:FROM|INTO|UPDATE|JOIN)\\s+[\\["`]?(?:\\w+[\\]"`]?\\.)?[\\["`]?(\\w+)') AS tbl,
                               if(SpanAttributes['url.full'] = '', SpanName,
                                  concat(SpanAttributes['http.request.method'], ' ',
                                         replaceRegexpAll(replaceRegexpAll(path(SpanAttributes['url.full']),
                                             '/[0-9]+(/|$)', '/{id}\\1'), '/[0-9]+(/|$)', '/{id}\\1'))) AS url_name,
                               if(SpanAttributes['server.port'] = '', SpanAttributes['server.address'],
                                  concat(SpanAttributes['server.address'], ':', SpanAttributes['server.port'])) AS host
                        FROM {{TracesTable}}
                        WHERE {{range}} AND ServiceName = {service:String}
                    ) AS c
                    LEFT JOIN (
                        SELECT TraceId, ParentSpanId, ServiceName AS callee_service, SpanName AS callee_op
                        FROM {{TracesTable}}
                        WHERE SpanKind = '{{ServerSpanKind}}'
                          AND Timestamp >= fromUnixTimestamp64Milli({fromMs:Int64} - {marginMs:Int64})
                          AND Timestamp <= fromUnixTimestamp64Milli({toMs:Int64} + {marginMs:Int64})
                    ) AS s ON s.TraceId = c.TraceId AND s.ParentSpanId = c.SpanId
                    WHERE {{spanFilter}}
                )
                """;
        }

        /// <summary>
        /// Bir endpoint'in (veya job'un) son <paramref name="limit"/> isteğinin kök span'leri ve bu trace'lerde
        /// servisin tüm span'leri. Ağaç C# tarafında kurulur (TraceBusiness.GetAnatomy).
        /// </summary>
        public async Task<(List<(string TraceId, string SpanId, double DurationMs)> Roots, List<AnatomySpan> Spans)> GetAnatomySpansAsync(
            AppKind app, string service, string operation, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default)
        {
            TraceFilter filter = new(app, from, to, service, operation);
            Dictionary<string, object> p = new() { ["limit"] = (uint)limit };
            string rootsSql = $$"""
                SELECT TraceId, SpanId, Duration / 1e6
                FROM {{TracesTable}}
                WHERE {{BuildWhere(filter, p)}}
                ORDER BY Timestamp DESC
                LIMIT {limit:UInt32}
                """;
            List<(string TraceId, string SpanId, double DurationMs)> roots = await db.QueryAsync(rootsSql, p, r =>
                (r.GetString(0), r.GetString(1), Convert.ToDouble(r.GetValue(2))), ct);
            if (roots.Count == 0) return (roots, []);

            Dictionary<string, object> q = [];
            string spansSql = $$"""
                SELECT TraceId, SpanId, ParentSpanId, Duration / 1e6, cat, op_name, target
                FROM {{ServiceSpansSource(service, from, to, q, roots.Select(r => r.TraceId).Distinct().ToArray())}}
                """;
            List<AnatomySpan> spans = await db.QueryAsync(spansSql, q, r => new AnatomySpan(
                r.GetString(0), r.GetString(1), NullIfEmpty(r.GetString(2)), Convert.ToDouble(r.GetValue(3)),
                r.GetString(4), r.GetString(5), r.GetString(6)), ct);
            return (roots, spans);
        }

        /// <summary>Metod / DB sorgusu / dış çağrı grupları, toplam süreye göre büyükten küçüğe.</summary>
        public Task<List<SpanGroupRow>> GetSpanGroupsAsync(string service, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            Dictionary<string, object> p = [];
            string sql = $$"""
                SELECT cat, op_name, target, count(), avg(Duration) / 1e6, quantile(0.95)(Duration) / 1e6, max(Duration) / 1e6,
                       countIf(StatusCode = '{{ErrorStatus}}'), sum(Duration) / 1e6
                FROM {{ServiceSpansSource(service, from, to, p)}}
                GROUP BY cat, op_name, target
                ORDER BY sum(Duration) DESC
                LIMIT {{SpanGroupLimit}}
                """;
            return db.QueryAsync(sql, p, r => new SpanGroupRow(
                r.GetString(0), r.GetString(1), r.GetString(2), Convert.ToInt64(r.GetValue(3)),
                Round(r.GetValue(4)), Round(r.GetValue(5)), Round(r.GetValue(6)), Convert.ToInt64(r.GetValue(7)), Round(r.GetValue(8))), ct);
        }

        /// <summary>
        /// Servisteki her span'in kendi süresi (çocukları hariç) türüne göre toplanır. Dış çağrı ve DB span'lerinin
        /// tamamı kendi türüne yazılır (bekleme karşı tarafta). "root" satırı ayrıca istek sayısı ve toplam süreyi verir.
        /// </summary>
        public Task<List<(string Category, double SelfMs, double TotalMs, long Count)>> GetTimeSplitAsync(
            string service, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new() { ["service"] = service };
            string range = RangeCondition(from, to, p);
            string sql = $$"""
                SELECT cat, sum(self) / 1e6, sum(Duration) / 1e6, count()
                FROM (
                    SELECT t.cat AS cat, t.Duration AS Duration,
                           if(t.cat IN ('{{SpanCategoryDb}}', '{{SpanCategoryCall}}'), toInt64(t.Duration),
                              greatest(toInt64(t.Duration) - toInt64(k.d), 0)) AS self
                    FROM (
                        SELECT TraceId, SpanId, Duration, {{SpanCategoryExpr}} AS cat
                        FROM {{TracesTable}}
                        WHERE {{range}} AND ServiceName = {service:String}
                    ) AS t
                    LEFT JOIN (
                        SELECT TraceId, ParentSpanId, sum(Duration) AS d
                        FROM {{TracesTable}}
                        WHERE {{range}} AND ServiceName = {service:String}
                        GROUP BY TraceId, ParentSpanId
                    ) AS k ON k.TraceId = t.TraceId AND k.ParentSpanId = t.SpanId
                )
                GROUP BY cat
                """;
            return db.QueryAsync(sql, p, r =>
                (r.GetString(0), Round(r.GetValue(1)), Round(r.GetValue(2)), Convert.ToInt64(r.GetValue(3))), ct);
        }

        /// <summary>
        /// Bir gruptaki en yavaş span'ler. Operation alanına span'in ait olduğu istek (endpoint/job) yazılır;
        /// aynı trace'te servise birden çok istek gelebildiği için span'in kendi atası bulunur.
        /// </summary>
        public async Task<List<RequestRowResponse>> GetSpanSamplesAsync(
            string service, string category, string name, string target, DateTimeOffset from, DateTimeOffset to, int limit,
            CancellationToken ct = default)
        {
            Dictionary<string, object> p = new()
            {
                ["category"] = category,
                ["name"] = name,
                ["target"] = target,
                ["limit"] = (uint)Math.Clamp(limit, 1, 50)
            };
            string sql = $$"""
                SELECT Timestamp, TraceId, SpanId, Duration / 1e6, StatusCode, StatusMessage, http_code
                FROM {{ServiceSpansSource(service, from, to, p)}}
                WHERE cat = {category:String} AND op_name = {name:String} AND target = {target:String}
                ORDER BY Duration DESC
                LIMIT {limit:UInt32}
                """;
            List<RequestRowResponse> rows = await db.QueryAsync(sql, p, r => new RequestRowResponse
            {
                Timestamp = r.GetDateTime(0),
                TraceId = r.GetString(1),
                SpanId = r.GetString(2),
                Service = service,
                DurationMs = Round(r.GetValue(3)),
                Status = r.GetString(4),
                StatusMessage = NullIfEmpty(r.GetString(5)),
                HttpStatusCode = NullIfEmpty(r.GetString(6))
            }, ct);
            if (rows.Count == 0) return rows;

            // Örneklerin trace'lerinde bu servisin span'leri: atadan köke yürüyüp isteğin adını bul
            Dictionary<string, object> q = new()
            {
                ["service"] = service,
                ["traceIds"] = rows.Select(r => r.TraceId).Distinct().ToArray()
            };
            string spansSql = $$"""
                SELECT SpanId, ParentSpanId, SpanName, SpanAttributes['{{JobNameAttribute}}'] != '' OR SpanKind = '{{ServerSpanKind}}'
                FROM {{TracesTable}}
                WHERE TraceId IN {traceIds:Array(String)} AND ServiceName = {service:String}
                """;
            Dictionary<string, (string? Parent, string Name, bool IsRoot)> spans = (await db.QueryAsync(spansSql, q, r =>
                (Id: r.GetString(0), Parent: NullIfEmpty(r.GetString(1)), Name: r.GetString(2), IsRoot: Convert.ToBoolean(r.GetValue(3))), ct))
                .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => (g.First().Parent, g.First().Name, g.First().IsRoot));

            foreach (RequestRowResponse row in rows)
            {
                string? current = row.SpanId;
                while (current is not null && spans.TryGetValue(current, out var span))
                {
                    if (span.IsRoot)
                    {
                        row.Operation = span.Name;
                        break;
                    }
                    current = span.Parent;
                }
            }
            return rows;
        }

        #endregion

        private static string RangeCondition(DateTimeOffset from, DateTimeOffset to, Dictionary<string, object> p)
        {
            p["fromMs"] = from.ToUnixTimeMilliseconds();
            p["toMs"] = to.ToUnixTimeMilliseconds();
            return "Timestamp >= fromUnixTimestamp64Milli({fromMs:Int64}) AND Timestamp <= fromUnixTimestamp64Milli({toMs:Int64})";
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

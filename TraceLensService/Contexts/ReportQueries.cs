using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Options;
using TraceLensService.Enums;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Contexts
{
    /// <summary>
    /// Raporların günlük özet tablosu (tracelens_daily). Ham span'ler 7 gün tutulduğu için haftalık/aylık karşılaştırma
    /// bu özetten yapılır. Materialized view otel_traces'e yazılan her kök span'i (istek / job çalışması) gün × operasyon
    /// satırına ekler: istek, toplam süre, hata, süre dağılımı (eşiği aşan sayısı için) ve yüzdelik durumu.
    /// </summary>
    public class ReportQueries(ClickHouseContext db, IOptions<ReportOptions> options, ILogger<ReportQueries> logger)
    {
        private const string Quantiles = "quantilesTDigest(0.5, 0.9, 0.95, 0.99)";

        public string TimeZone => options.Value.TimeZone;

        /// <summary>
        /// Tablo ve view yoksa oluşturur. View ilk kez oluşturuluyorsa mevcut ham veri (son 7 gün) özet tabloya bir kez
        /// aktarılır; view'in yakaladığı aralıkla çakışmasın diye view oluşturulmadan önceki ana kadar.
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            await db.ExecuteAsync($"""
                CREATE TABLE IF NOT EXISTS {DailyTable}
                (
                    Day          Date,
                    App          LowCardinality(String),
                    Service      LowCardinality(String),
                    Operation    String,
                    Requests     SimpleAggregateFunction(sum, UInt64),
                    DurationSum  SimpleAggregateFunction(sum, UInt64),
                    Errors       SimpleAggregateFunction(sum, UInt64),
                    Histogram    SimpleAggregateFunction(sumMap, Tuple(Array(UInt8), Array(UInt64))),
                    Quantiles    AggregateFunction({Quantiles}, UInt64)
                )
                ENGINE = AggregatingMergeTree
                ORDER BY (Day, App, Service, Operation)
                TTL Day + INTERVAL {DailyRetentionDays} DAY
                """, null, ct);

            Dictionary<string, object> p = new() { ["view"] = DailyView };
            bool viewExists = (await db.QueryAsync("SELECT count() FROM system.tables WHERE database = currentDatabase() AND name = {view:String}",
                p, r => Convert.ToInt64(r.GetValue(0)), ct))[0] > 0;
            if (viewExists) return;

            long cutoffMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.ExecuteAsync($"CREATE MATERIALIZED VIEW IF NOT EXISTS {DailyView} TO {DailyTable} AS {RollupSelect("1")}", null, ct);
            await db.ExecuteAsync($"INSERT INTO {DailyTable} {RollupSelect("Timestamp < fromUnixTimestamp64Milli({cutoff:Int64})")}",
                new Dictionary<string, object> { ["cutoff"] = cutoffMs }, ct);
            logger.LogInformation("Günlük rapor özeti oluşturuldu ve mevcut veri aktarıldı ({TimeZone})", TimeZone);
        }

        // View ve ilk aktarımın ortak SELECT'i. View tanımına parametre konamadığı için sabitler metne gömülür.
        private string RollupSelect(string extraCondition)
        {
            string edges = string.Join(", ", HistogramEdgesMs.Select(ms => ((ulong)(ms * 1_000_000)).ToString(CultureInfo.InvariantCulture)));
            string tz = TimeZone.Replace("'", string.Empty);
            return $"""
                SELECT toDate(Timestamp, '{tz}') AS Day, {TraceQueries.AppKindExpr} AS App, ServiceName AS Service, SpanName AS Operation,
                       count() AS Requests, sum(Duration) AS DurationSum, countIf(StatusCode = '{ErrorStatus}') AS Errors,
                       sumMap([toUInt8(arrayFirstIndex(e -> Duration < e, [{edges}]))], [toUInt64(1)]) AS Histogram,
                       {Quantiles.Replace("quantilesTDigest", "quantilesTDigestState")}(Duration) AS Quantiles
                FROM {TracesTable}
                WHERE {TraceQueries.AnyRootCondition} AND {extraCondition}
                GROUP BY Day, App, Service, Operation
                """;
        }

        public enum Grouping { Operation, Day, DayOperation, Total }

        /// <summary>[from, to] günlerinin (ikisi dahil) özeti, istenen kırılımda.</summary>
        public Task<List<DailyRow>> GetAsync(DateOnly from, DateOnly to, Grouping grouping, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new()
            {
                ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            bool byDay = grouping is Grouping.Day or Grouping.DayOperation;
            bool byOperation = grouping is Grouping.Operation or Grouping.DayOperation;
            List<string> keys = [];
            if (byDay) keys.Add("Day");
            if (byOperation) keys.AddRange(["App", "Service", "Operation"]);

            string sql = $$"""
                SELECT {{(byDay ? "toString(Day)" : "''")}}, {{(byOperation ? "App, Service, Operation" : "'Service', '', ''")}},
                       sum(Requests), sum(DurationSum) / 1e6, sum(Errors),
                       sumMap(Histogram) AS h, h.1, h.2,
                       {{Quantiles.Replace("quantilesTDigest", "quantilesTDigestMerge")}}(Quantiles) AS q, q[1] / 1e6, q[2] / 1e6, q[3] / 1e6, q[4] / 1e6
                FROM {{DailyTable}}
                WHERE Day >= toDate({from:String}) AND Day <= toDate({to:String})
                {{(keys.Count > 0 ? $"GROUP BY {string.Join(", ", keys)}" : string.Empty)}}
                """;
            return db.QueryAsync(sql, p, r =>
            {
                long requests = Convert.ToInt64(r.GetValue(4));
                Dictionary<int, long> histogram = [];
                if (r.GetValue(8) is IList bucketIndexes && r.GetValue(9) is IList counts)
                    for (int i = 0; i < bucketIndexes.Count; i++)
                        histogram[Convert.ToInt32(bucketIndexes[i])] = Convert.ToInt64(counts[i]);
                string day = r.GetString(0);
                return new DailyRow(
                    day.Length > 0 ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    Enum.Parse<AppKind>(r.GetString(1)), r.GetString(2), r.GetString(3),
                    requests, Convert.ToDouble(r.GetValue(5)), Convert.ToInt64(r.GetValue(6)), histogram,
                    requests == 0 ? 0 : Round(r.GetValue(11)), requests == 0 ? 0 : Round(r.GetValue(12)),
                    requests == 0 ? 0 : Round(r.GetValue(13)), requests == 0 ? 0 : Round(r.GetValue(14)));
            }, ct);
        }

        /// <summary>Özet tablodaki ilk gün (veri yoksa null).</summary>
        public async Task<DateOnly?> GetFirstDayAsync(CancellationToken ct = default)
        {
            List<string> rows = await db.QueryAsync($"SELECT if(count() = 0, '', toString(min(Day))) FROM {DailyTable}",
                new Dictionary<string, object>(), r => r.GetString(0), ct);
            return rows[0].Length > 0 ? DateOnly.ParseExact(rows[0], "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// Süre dağılımından eşiği aşan istek sayısı: alt sınırı eşiğe eşit ya da büyük aralıklar. Eşik bir aralık
        /// sınırına denk gelmiyorsa (ör. 250 ms) içinde kaldığı aralık sayılmaz; sonuç yaklaşıktır.
        /// </summary>
        public static long SlowCount(Dictionary<int, long> histogram, double thresholdMs) =>
            histogram.Where(b => LowerEdgeMs(b.Key) >= thresholdMs).Sum(b => b.Value);

        private static double LowerEdgeMs(int index) => index switch
        {
            0 => HistogramEdgesMs[^1],
            1 => 0,
            _ => HistogramEdgesMs[index - 2]
        };

        private static double Round(object value) => Math.Round(Convert.ToDouble(value), 2);
    }
}

using System.Data.Common;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Contexts
{
    /// <summary>
    /// tracelens_alerts tablosu. Her alarm tek satırdır; güncellemeler yüksek <c>Version</c> ile
    /// yeniden yazılır ve ReplacingMergeTree + FINAL ile son hali okunur.
    /// </summary>
    public class AlertQueries(ClickHouseContext db)
    {
        private const string Columns =
            "Id, Key, App, Service, Operation, Metric, ValueMs, PeakValueMs, ThresholdMs, " +
            "RequestCount, SlowCount, FiredAt, LastCheckedAt, ResolvedAt";

        public Task EnsureSchemaAsync(CancellationToken ct = default) => db.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {AlertsTable}
            (
                Id            UUID,
                Key           String,
                App           LowCardinality(String),
                Service       LowCardinality(String),
                Operation     String,
                Metric        LowCardinality(String),
                ValueMs       Float64,
                PeakValueMs   Float64,
                ThresholdMs   Float64,
                RequestCount  UInt64,
                SlowCount     UInt64,
                FiredAt       DateTime64(3, 'UTC'),
                LastCheckedAt DateTime64(3, 'UTC'),
                ResolvedAt    Nullable(DateTime64(3, 'UTC')),
                Version       UInt64
            )
            ENGINE = ReplacingMergeTree(Version)
            ORDER BY Id
            TTL toDateTime(FiredAt) + INTERVAL {AlertRetentionDays} DAY
            """, null, ct);

        public Task SaveAsync(AlertRecord a, CancellationToken ct = default)
        {
            Dictionary<string, object> p = new()
            {
                ["id"] = a.Id.ToString(),
                ["key"] = a.Key,
                ["app"] = a.App.ToString(),
                ["service"] = a.Service,
                ["operation"] = a.Operation,
                ["metric"] = a.Metric,
                ["value"] = a.ValueMs,
                ["peak"] = a.PeakValueMs,
                ["threshold"] = a.ThresholdMs,
                ["requests"] = (ulong)a.RequestCount,
                ["slow"] = (ulong)a.SlowCount,
                ["firedMs"] = ToUnixMs(a.FiredAt),
                ["checkedMs"] = ToUnixMs(a.LastCheckedAt),
                ["resolvedMs"] = a.ResolvedAt is DateTime r ? ToUnixMs(r) : 0L,
                ["version"] = (ulong)DateTime.UtcNow.Ticks
            };

            return db.ExecuteAsync($$"""
                INSERT INTO {{AlertsTable}} ({{Columns}}, Version)
                SELECT toUUID({id:String}), {key:String}, {app:String}, {service:String}, {operation:String},
                       {metric:String}, {value:Float64}, {peak:Float64}, {threshold:Float64},
                       {requests:UInt64}, {slow:UInt64},
                       fromUnixTimestamp64Milli({firedMs:Int64}, 'UTC'),
                       fromUnixTimestamp64Milli({checkedMs:Int64}, 'UTC'),
                       if({resolvedMs:Int64} = 0, NULL, fromUnixTimestamp64Milli({resolvedMs:Int64}, 'UTC')),
                       {version:UInt64}
                """, p, ct);
        }

        public Task<List<AlertRecord>> GetActiveAsync(CancellationToken ct = default) => db.QueryAsync(
            $"SELECT {Columns} FROM {AlertsTable} FINAL WHERE ResolvedAt IS NULL",
            new Dictionary<string, object>(), Map, ct);

        public Task<List<AlertRecord>> GetHistoryAsync(int days, int limit, CancellationToken ct = default) => db.QueryAsync(
            $$"""
            SELECT {{Columns}} FROM {{AlertsTable}} FINAL
            WHERE ResolvedAt IS NOT NULL AND FiredAt > now() - toIntervalDay({days:UInt32})
            ORDER BY FiredAt DESC
            LIMIT {limit:UInt32}
            """,
            new Dictionary<string, object>
            {
                ["days"] = (uint)Math.Clamp(days, 1, AlertRetentionDays),
                ["limit"] = (uint)Math.Clamp(limit, 1, 1000)
            },
            Map, ct);

        private static AlertRecord Map(DbDataReader r) => new(
            r.GetGuid(0),
            r.GetString(1),
            Enum.Parse<AppKind>(r.GetString(2)),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.GetDouble(6),
            r.GetDouble(7),
            r.GetDouble(8),
            Convert.ToInt64(r.GetValue(9)),
            Convert.ToInt64(r.GetValue(10)),
            Utc(r.GetDateTime(11)),
            Utc(r.GetDateTime(12)),
            r.IsDBNull(13) ? null : Utc(r.GetDateTime(13)));

        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static long ToUnixMs(DateTime value) =>
            new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }
}

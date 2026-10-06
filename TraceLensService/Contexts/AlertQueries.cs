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
            "RequestCount, SlowCount, FiredAt, LastCheckedAt, ResolvedAt, Kind, ErrorCount, ErrorRate, PeakErrorRate, TopStatus";

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            await CreateTableAsync(ct);
            // Hata alarmları sonradan eklendi: eski kurulumlarda sütunlar yoksa eklenir, eski alarmlar "slow" sayılır
            await db.ExecuteAsync($"""
                ALTER TABLE {AlertsTable}
                    ADD COLUMN IF NOT EXISTS Kind LowCardinality(String) DEFAULT 'slow',
                    ADD COLUMN IF NOT EXISTS ErrorCount UInt64 DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ErrorRate Float64 DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS PeakErrorRate Float64 DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS TopStatus String DEFAULT ''
                """, null, ct);
        }

        private Task CreateTableAsync(CancellationToken ct) => db.ExecuteAsync($"""
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
                ["version"] = (ulong)DateTime.UtcNow.Ticks,
                ["kind"] = a.Kind,
                ["errors"] = (ulong)a.ErrorCount,
                ["errorRate"] = a.ErrorRate,
                ["peakErrorRate"] = a.PeakErrorRate,
                ["topStatus"] = a.TopStatus
            };

            return db.ExecuteAsync($$"""
                INSERT INTO {{AlertsTable}} ({{Columns}}, Version)
                SELECT toUUID({id:String}), {key:String}, {app:String}, {service:String}, {operation:String},
                       {metric:String}, {value:Float64}, {peak:Float64}, {threshold:Float64},
                       {requests:UInt64}, {slow:UInt64},
                       fromUnixTimestamp64Milli({firedMs:Int64}, 'UTC'),
                       fromUnixTimestamp64Milli({checkedMs:Int64}, 'UTC'),
                       if({resolvedMs:Int64} = 0, NULL, fromUnixTimestamp64Milli({resolvedMs:Int64}, 'UTC')),
                       {kind:String}, {errors:UInt64}, {errorRate:Float64}, {peakErrorRate:Float64}, {topStatus:String},
                       {version:UInt64}
                """, p, ct);
        }

        public Task<List<AlertRecord>> GetActiveAsync(CancellationToken ct = default) => db.QueryAsync(
            $"SELECT {Columns} FROM {AlertsTable} FINAL WHERE ResolvedAt IS NULL",
            new Dictionary<string, object>(), Map, ct);


        /// <summary>[from, to) aralığında açık kalmış (o aralıkta açılan ya da hâlâ süren) alarmlar; raporlar için.</summary>
        public Task<List<AlertRecord>> GetOverlappingAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => db.QueryAsync(
            $$"""
            SELECT {{Columns}} FROM {{AlertsTable}} FINAL
            WHERE FiredAt < fromUnixTimestamp64Milli({toMs:Int64}, 'UTC')
              AND (ResolvedAt IS NULL OR ResolvedAt > fromUnixTimestamp64Milli({fromMs:Int64}, 'UTC'))
            ORDER BY FiredAt
            LIMIT 1000
            """,
            new Dictionary<string, object> { ["fromMs"] = from.ToUnixTimeMilliseconds(), ["toMs"] = to.ToUnixTimeMilliseconds() },
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
            r.IsDBNull(13) ? null : Utc(r.GetDateTime(13)),
            r.GetString(14),
            Convert.ToInt64(r.GetValue(15)),
            r.GetDouble(16),
            r.GetDouble(17),
            r.GetString(18));

        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static long ToUnixMs(DateTime value) =>
            new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }
}

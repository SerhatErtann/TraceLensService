using TraceLensService.Models.DbModels;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Contexts
{
    /// <summary>
    /// tracelens_thresholds tablosu. Anahtar başına tek satır; güncelleme ve silme yüksek <c>Version</c> ile
    /// yeni satır yazılarak yapılır (silme = IsDeleted 1), ReplacingMergeTree + FINAL son hali verir.
    /// </summary>
    public class ThresholdQueries(ClickHouseContext db)
    {
        public Task EnsureSchemaAsync(CancellationToken ct = default) => db.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {ThresholdsTable}
            (
                Key         String,
                Service     String,
                Operation   String,
                ThresholdMs Float64,
                UpdatedAt   DateTime64(3, 'UTC'),
                IsDeleted   UInt8,
                Version     UInt64
            )
            ENGINE = ReplacingMergeTree(Version)
            ORDER BY Key
            """, null, ct);

        /// <summary>Silinmiş olanlar dahil hiç satır var mı? (İlk açılışta config'ten doldurma kararı için.)</summary>
        public async Task<bool> HasAnyRowAsync(CancellationToken ct = default) =>
            (await db.QueryAsync($"SELECT count() FROM {ThresholdsTable}", new Dictionary<string, object>(),
                r => Convert.ToInt64(r.GetValue(0)), ct))[0] > 0;

        public Task<List<ThresholdRecord>> GetAllAsync(CancellationToken ct = default) => db.QueryAsync(
            $"SELECT Service, Operation, ThresholdMs, UpdatedAt FROM {ThresholdsTable} FINAL WHERE IsDeleted = 0 ORDER BY Service, Operation",
            new Dictionary<string, object>(),
            r => new ThresholdRecord(r.GetString(0), r.GetString(1), r.GetDouble(2),
                DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)),
            ct);

        public Task UpsertAsync(string service, string operation, double thresholdMs, CancellationToken ct = default) =>
            WriteAsync(service, operation, thresholdMs, isDeleted: false, ct);

        public Task DeleteAsync(string service, string operation, CancellationToken ct = default) =>
            WriteAsync(service, operation, 0, isDeleted: true, ct);

        public static string KeyFor(string service, string operation) =>
            service.Length == 0 && operation.Length == 0 ? DefaultThresholdKey : $"{service}|{operation}";

        private Task WriteAsync(string service, string operation, double thresholdMs, bool isDeleted, CancellationToken ct)
        {
            Dictionary<string, object> p = new()
            {
                ["key"] = KeyFor(service, operation),
                ["service"] = service,
                ["operation"] = operation,
                ["threshold"] = thresholdMs,
                ["updatedMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["deleted"] = (byte)(isDeleted ? 1 : 0),
                ["version"] = (ulong)DateTime.UtcNow.Ticks
            };

            return db.ExecuteAsync($$"""
                INSERT INTO {{ThresholdsTable}} (Key, Service, Operation, ThresholdMs, UpdatedAt, IsDeleted, Version)
                SELECT {key:String}, {service:String}, {operation:String}, {threshold:Float64},
                       fromUnixTimestamp64Milli({updatedMs:Int64}, 'UTC'), {deleted:UInt8}, {version:UInt64}
                """, p, ct);
        }
    }
}

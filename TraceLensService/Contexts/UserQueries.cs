using TraceLensService.Models.DbModels;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Contexts
{
    /// <summary>
    /// tracelens_users tablosu. Kullanıcı başına tek satır; şifre değişikliği ve silme yüksek <c>Version</c> ile yeni satır
    /// yazılarak yapılır (silme = IsDeleted 1), ReplacingMergeTree + FINAL son hali verir (eşik tablosuyla aynı yöntem).
    /// </summary>
    public class UserQueries(ClickHouseContext db)
    {
        public Task EnsureSchemaAsync(CancellationToken ct = default) => db.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {UsersTable}
            (
                Username     String,
                PasswordHash String,
                CreatedAt    DateTime64(3, 'UTC'),
                IsDeleted    UInt8,
                Version      UInt64
            )
            ENGINE = ReplacingMergeTree(Version)
            ORDER BY Username
            """, null, ct);

        public Task<List<UserRecord>> GetAllAsync(CancellationToken ct = default) => db.QueryAsync(
            $"SELECT Username, PasswordHash, CreatedAt FROM {UsersTable} FINAL WHERE IsDeleted = 0 ORDER BY Username",
            new Dictionary<string, object>(),
            r => new UserRecord(r.GetString(0), r.GetString(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc)),
            ct);

        public Task UpsertAsync(UserRecord user, CancellationToken ct = default) => WriteAsync(user, isDeleted: false, ct);

        public Task DeleteAsync(UserRecord user, CancellationToken ct = default) => WriteAsync(user with { PasswordHash = string.Empty }, isDeleted: true, ct);

        private Task WriteAsync(UserRecord user, bool isDeleted, CancellationToken ct)
        {
            Dictionary<string, object> p = new()
            {
                ["username"] = user.Username,
                ["hash"] = user.PasswordHash,
                ["createdMs"] = new DateTimeOffset(user.CreatedAt).ToUnixTimeMilliseconds(),
                ["deleted"] = (byte)(isDeleted ? 1 : 0),
                ["version"] = (ulong)DateTime.UtcNow.Ticks
            };

            return db.ExecuteAsync($$"""
                INSERT INTO {{UsersTable}} (Username, PasswordHash, CreatedAt, IsDeleted, Version)
                SELECT {username:String}, {hash:String}, fromUnixTimestamp64Milli({createdMs:Int64}, 'UTC'), {deleted:UInt8}, {version:UInt64}
                """, p, ct);
        }
    }
}

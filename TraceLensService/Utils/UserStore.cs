using Microsoft.Extensions.Options;
using TraceLensService.Contexts;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Options;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Utils
{
    public enum AddUserResult { Added, UsernameTaken, NotFirst }

    /// <summary>
    /// Dashboard kullanıcıları (tracelens_users). Liste bellekte kısa süre tutulur; her istekte oturumun hâlâ geçerli olduğu
    /// buradan kontrol edilir. Birden fazla TraceLensService örneği varsa diğerinin değişikliği en geç UserCacheSeconds'ta görülür.
    /// </summary>
    public class UserStore(UserQueries queries, IOptions<AuthOptions> options, ILogger<UserStore> logger)
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Dictionary<string, UserRecord>? _users;
        private DateTime _loadedAt;
        private bool _schemaReady;

        /// <summary>Kullanıcı adları küçük harfle saklanır; "Serhat" ve "serhat" aynı hesaptır.</summary>
        public static string Normalize(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

        public async Task<IReadOnlyCollection<UserRecord>> GetAllAsync(CancellationToken ct = default) =>
            (await GetMapAsync(ct)).Values;

        public async Task<UserRecord?> FindAsync(string? username, CancellationToken ct = default) =>
            (await GetMapAsync(ct)).GetValueOrDefault(Normalize(username));

        /// <summary>Kullanıcı ekler. <paramref name="onlyIfFirst"/>: yalnızca hiç kullanıcı yokken (ilk hesap; aynı anda iki kayıt yarışmasın).</summary>
        public async Task<(AddUserResult Result, UserRecord? User)> AddAsync(string username, string password, bool onlyIfFirst, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                await LoadAsync(ct);
                if (onlyIfFirst && _users!.Count > 0)
                    return (AddUserResult.NotFirst, null);
                if (_users!.ContainsKey(username))
                    return (AddUserResult.UsernameTaken, null);

                UserRecord user = new(username, PasswordHasher.Hash(password), DateTime.UtcNow);
                await queries.UpsertAsync(user, ct);
                _users[username] = user;
                return (AddUserResult.Added, user);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<UserRecord> ChangePasswordAsync(UserRecord user, string newPassword, CancellationToken ct = default)
        {
            UserRecord updated = user with { PasswordHash = PasswordHasher.Hash(newPassword) };
            await WriteLockedAsync(() => queries.UpsertAsync(updated, ct), u => u[user.Username] = updated, ct);
            return updated;
        }

        public Task DeleteAsync(UserRecord user, CancellationToken ct = default) =>
            WriteLockedAsync(() => queries.DeleteAsync(user, ct), u => u.Remove(user.Username), ct);

        private async Task WriteLockedAsync(Func<Task> write, Action<Dictionary<string, UserRecord>> apply, CancellationToken ct)
        {
            await _lock.WaitAsync(ct);
            try
            {
                await write();
                if (_users is not null) apply(_users);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<Dictionary<string, UserRecord>> GetMapAsync(CancellationToken ct)
        {
            if (_users is not null && DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(UserCacheSeconds))
                return _users;

            await _lock.WaitAsync(ct);
            try
            {
                if (_users is null || DateTime.UtcNow - _loadedAt >= TimeSpan.FromSeconds(UserCacheSeconds))
                    await LoadAsync(ct);
                return _users!;
            }
            finally
            {
                _lock.Release();
            }
        }

        // _lock tutulurken çağrılır
        private async Task LoadAsync(CancellationToken ct)
        {
            if (!_schemaReady)
            {
                await queries.EnsureSchemaAsync(ct);
                _schemaReady = true;
            }

            List<UserRecord> rows = await queries.GetAllAsync(ct);

            // Sunucu kurulumu: hiç kullanıcı yoksa ve ortam değişkeninde şifre verildiyse ilk hesap ondan açılır
            // (böylece sunucu ilk açıldığında "ilk hesabı oluştur" ekranı herkese açık kalmaz)
            AuthOptions opts = options.Value;
            if (rows.Count == 0 && !string.IsNullOrEmpty(opts.Password))
            {
                UserRecord seed = new(Normalize(opts.Username), PasswordHasher.Hash(opts.Password), DateTime.UtcNow);
                await queries.UpsertAsync(seed, ct);
                rows.Add(seed);
                logger.LogInformation("İlk kullanıcı ortam değişkeninden oluşturuldu: {Username}", seed.Username);
            }

            _users = rows.ToDictionary(u => u.Username);
            _loadedAt = DateTime.UtcNow;
        }
    }
}

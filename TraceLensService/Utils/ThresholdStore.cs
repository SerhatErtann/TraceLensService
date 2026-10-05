using Microsoft.Extensions.Options;
using TraceLensService.Contexts;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Options;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Geçerli eşiklerin bellekteki anlık görüntüsü. Sorgular ve alarm kontrolü buradan okur.
    /// ClickHouse'tan yüklenene kadar appsettings "Thresholds" değerleri kullanılır.
    /// </summary>
    public class ThresholdStore(ThresholdQueries queries, IOptions<ThresholdOptions> configured)
    {
        private volatile Snapshot _snapshot = new(configured.Value, []);

        /// <summary>Sorgularda kullanılacak eşikler (varsayılan + özel).</summary>
        public ThresholdOptions Current => _snapshot.Options;

        /// <summary>Dashboard'da listelenecek özel eşikler (güncellenme zamanıyla).</summary>
        public IReadOnlyList<ThresholdRecord> Overrides => _snapshot.Overrides;

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            List<ThresholdRecord> rows = await queries.GetAllAsync(ct);
            ThresholdRecord? defaultRow = rows.FirstOrDefault(r => r.IsDefault);
            List<ThresholdRecord> overrides = rows.Where(r => !r.IsDefault).ToList();

            ThresholdOptions options = new()
            {
                DefaultMs = defaultRow?.ThresholdMs ?? configured.Value.DefaultMs,
                Overrides = overrides.ToDictionary(r => ThresholdQueries.KeyFor(r.Service, r.Operation), r => r.ThresholdMs)
            };
            _snapshot = new Snapshot(options, overrides);
        }

        /// <summary>Tablo hiç kullanılmamışsa appsettings'teki eşikleri bir kereye mahsus aktarır.</summary>
        public async Task SeedFromConfigIfEmptyAsync(CancellationToken ct = default)
        {
            if (await queries.HasAnyRowAsync(ct)) return;

            await queries.UpsertAsync(string.Empty, string.Empty, configured.Value.DefaultMs, ct);
            foreach ((string key, double ms) in configured.Value.Overrides)
            {
                string[] parts = key.Split('|', 2);
                if (parts.Length == 2)
                    await queries.UpsertAsync(parts[0], parts[1], ms, ct);
            }
        }

        private sealed record Snapshot(ThresholdOptions Options, IReadOnlyList<ThresholdRecord> Overrides);
    }
}

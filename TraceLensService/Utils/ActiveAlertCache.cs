using System.Collections.Concurrent;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Açık alarmların bellekteki kopyası. Açılışta ClickHouse'tan yüklenir; her değişiklik
    /// AlertQueries ile kalıcı hale getirilir (bkz. AlertWorker).
    /// </summary>
    public class ActiveAlertCache
    {
        private readonly ConcurrentDictionary<string, AlertRecord> _active = new();

        // Önce hata alarmları (orana göre), sonra yavaşlık (eşiği ne kadar aştığına göre)
        public List<AlertRecord> Active => _active.Values
            .OrderByDescending(a => a.Kind == AlertRecord.ErrorKind)
            .ThenByDescending(a => a.Kind == AlertRecord.ErrorKind ? a.ErrorRate : a.ValueMs / a.ThresholdMs)
            .ToList();

        public bool IsActive(string key) => _active.ContainsKey(key);

        public void Load(IEnumerable<AlertRecord> active)
        {
            _active.Clear();
            foreach (AlertRecord alert in active)
                _active[alert.Key] = alert;
        }

        /// <summary>Alarmı açar ya da günceller. Yeni açıldıysa <c>IsNew</c> true döner.</summary>
        public (AlertRecord Alert, bool IsNew) Upsert(
            string key, AppKind app, OperationSummaryResponse row, string metric, double value, DateTime now)
        {
            if (_active.TryGetValue(key, out AlertRecord? existing))
            {
                AlertRecord updated = existing with
                {
                    ValueMs = value,
                    PeakValueMs = Math.Max(existing.PeakValueMs, value),
                    ThresholdMs = row.ThresholdMs,
                    RequestCount = row.Count,
                    SlowCount = row.SlowCount,
                    LastCheckedAt = now
                };
                _active[key] = updated;
                return (updated, false);
            }

            AlertRecord alert = new(Guid.NewGuid(), key, app, row.Service, row.Operation, metric,
                value, value, row.ThresholdMs, row.Count, row.SlowCount, now, now);
            _active[key] = alert;
            return (alert, true);
        }

        /// <summary>Hata alarmını açar ya da günceller (hata oranı, en sık sonuç kodu).</summary>
        public (AlertRecord Alert, bool IsNew) UpsertError(
            string key, AppKind app, OperationSummaryResponse row, string topStatus, DateTime now)
        {
            if (_active.TryGetValue(key, out AlertRecord? existing))
            {
                AlertRecord updated = existing with
                {
                    ValueMs = row.AvgMs,
                    PeakValueMs = Math.Max(existing.PeakValueMs, row.AvgMs),
                    ThresholdMs = row.ThresholdMs,
                    RequestCount = row.Count,
                    SlowCount = row.SlowCount,
                    ErrorCount = row.ErrorCount,
                    ErrorRate = row.ErrorRate,
                    PeakErrorRate = Math.Max(existing.PeakErrorRate, row.ErrorRate),
                    TopStatus = string.IsNullOrEmpty(topStatus) ? existing.TopStatus : topStatus,
                    LastCheckedAt = now
                };
                _active[key] = updated;
                return (updated, false);
            }

            AlertRecord alert = new(Guid.NewGuid(), key, app, row.Service, row.Operation, "errors",
                row.AvgMs, row.AvgMs, row.ThresholdMs, row.Count, row.SlowCount, now, now,
                Kind: AlertRecord.ErrorKind, ErrorCount: row.ErrorCount, ErrorRate: row.ErrorRate,
                PeakErrorRate: row.ErrorRate, TopStatus: topStatus);
            _active[key] = alert;
            return (alert, true);
        }

        public List<AlertRecord> ResolveMissing(IReadOnlySet<string> stillFiring, DateTime now)
        {
            List<AlertRecord> resolved = [];
            foreach (string key in _active.Keys.Where(k => !stillFiring.Contains(k)).ToList())
            {
                if (_active.TryRemove(key, out AlertRecord? alert))
                    resolved.Add(alert with { ResolvedAt = now, LastCheckedAt = now });
            }
            return resolved;
        }
    }
}

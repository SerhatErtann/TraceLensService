using CommonUtils.Models;
using Microsoft.Extensions.Options;
using TraceLensService.Common;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Options;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Alerts;
using TraceLensService.Models.Responses.Settings;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Alarmlar sayfası ve dashboard ayarları için iş katmanı.</summary>
    public class AlertBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<AlertBusiness>>())
    {
        private readonly AlertQueries _alertQueries = serviceProvider.GetRequiredService<AlertQueries>();
        private readonly ActiveAlertCache _activeAlerts = serviceProvider.GetRequiredService<ActiveAlertCache>();
        private readonly AlertNotifier _notifier = serviceProvider.GetRequiredService<AlertNotifier>();
        private readonly ThresholdStore _thresholds = serviceProvider.GetRequiredService<ThresholdStore>();
        private readonly IOptionsMonitor<AlertOptions> _alertOptions = serviceProvider.GetRequiredService<IOptionsMonitor<AlertOptions>>();
        private readonly IOptionsMonitor<NotificationOptions> _notificationOptions = serviceProvider.GetRequiredService<IOptionsMonitor<NotificationOptions>>();

        /// <summary>
        /// Açık alarmlar bellekten, kapananlar ClickHouse'tan (aralıkta açık kalmış olanlar). Filtreler ikisine de uygulanır;
        /// alarm sayısı küçük olduğu için (90 gün saklanır) bellekte filtrelenir.
        /// </summary>
        public Task<DataResponse<AlertListResponse>> GetAlerts(AlertFilterRequest request)
            => Guard(async () =>
            {
                DataResponse<AlertListResponse> response = new();
                DateTimeOffset to = request.To ?? DateTimeOffset.UtcNow;
                DateTimeOffset from = request.From ?? to.AddDays(-Math.Clamp(request.Days ?? DefaultAlertHistoryDays, 1, AlertRetentionDays));

                List<AlertRecord> active = _activeAlerts.Active;
                List<AlertRecord> history = (await _alertQueries.GetOverlappingAsync(from, to))
                    .Where(a => a.ResolvedAt is not null)
                    .OrderByDescending(a => a.FiredAt)
                    .ToList();
                List<AlertRecord> all = [.. active, .. history];

                response.Success(new AlertListResponse
                {
                    From = from.UtcDateTime,
                    To = to.UtcDateTime,
                    Active = active.Where(a => Matches(a, request)).ToList(),
                    History = history.Where(a => Matches(a, request)).Take(AlertHistoryLimit).ToList(),
                    Services = all.Select(a => a.Service).Distinct().Order().ToList(),
                    Statuses = all.Select(a => a.TopStatus).Where(s => s.Length > 0).Distinct().Order().ToList()
                });
                return response;
            }, GeneralConsts.AlertsNotRetrieved);

        private static bool Matches(AlertRecord a, AlertFilterRequest f)
        {
            if (f.App is { Length: > 0 } app && !a.App.ToString().Equals(app, StringComparison.OrdinalIgnoreCase)) return false;
            if (f.Service is { Length: > 0 } service && a.Service != service) return false;
            if (f.Operation is { Length: > 0 } op && !a.Operation.Contains(op, StringComparison.OrdinalIgnoreCase)) return false;
            if (f.Kind is { Length: > 0 } kind && a.Kind != kind) return false;
            if (f.MinPeakMs is double min && a.PeakValueMs < min) return false;
            if (f.Status is { Length: > 0 } status)
            {
                // "5xx" = 500–599 arası her kod; aksi halde tam eşleşme
                bool classMatch = status.Length == 3 && status.EndsWith("xx", StringComparison.OrdinalIgnoreCase)
                    && a.TopStatus.Length == 3 && a.TopStatus[0] == status[0] && a.TopStatus.All(char.IsDigit);
                if (!classMatch && !a.TopStatus.Equals(status, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>Webhook ayarını denemek için örnek bir alarm bildirimi gönderir.</summary>
        public Task<BaseResponse> SendTestNotification()
            => Guard(async () =>
            {
                BaseResponse response = new();
                DateTime now = DateTime.UtcNow;
                AlertRecord sample = new(Guid.NewGuid(), "test", AppKind.Service, "ornek-servis", "GET /ornek/endpoint", "avg",
                    350, 420, 200, 120, 80, now.AddMinutes(-3), now);

                string? error = await _notifier.NotifyAsync(AlertNotifier.Test, sample);
                if (error is not null)
                    throw new FriendlyException(error);

                response.Success(GeneralConsts.NotificationSent);
                return response;
            });

        public Task<DataResponse<SettingsResponse>> GetSettings()
            => Guard(() =>
            {
                DataResponse<SettingsResponse> response = new();
                response.Success(new SettingsResponse
                {
                    DefaultThresholdMs = _thresholds.Current.DefaultMs,
                    ThresholdOverrides = _thresholds.Current.Overrides,
                    AlertMetric = _alertOptions.CurrentValue.Metric,
                    AlertWindowMinutes = _alertOptions.CurrentValue.WindowMinutes,
                    NotificationsConfigured = _notifier.IsConfigured,
                    NotificationFormat = _notificationOptions.CurrentValue.Format
                });
                return Task.FromResult(response);
            });
    }
}

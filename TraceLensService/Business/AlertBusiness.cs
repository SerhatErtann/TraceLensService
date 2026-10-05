using CommonUtils.Models;
using Microsoft.Extensions.Options;
using TraceLensService.Common;
using TraceLensService.Contexts;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Options;
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
        private readonly IOptionsMonitor<ThresholdOptions> _thresholds = serviceProvider.GetRequiredService<IOptionsMonitor<ThresholdOptions>>();
        private readonly IOptionsMonitor<AlertOptions> _alertOptions = serviceProvider.GetRequiredService<IOptionsMonitor<AlertOptions>>();
        private readonly IOptionsMonitor<NotificationOptions> _notificationOptions = serviceProvider.GetRequiredService<IOptionsMonitor<NotificationOptions>>();

        /// <summary>Açık alarmlar bellekten, geçmiş ClickHouse'tan.</summary>
        public Task<DataResponse<AlertListResponse>> GetAlerts(int? days)
            => Guard(async () =>
            {
                DataResponse<AlertListResponse> response = new();
                response.Success(new AlertListResponse
                {
                    Active = _activeAlerts.Active,
                    History = await _alertQueries.GetHistoryAsync(days ?? DefaultAlertHistoryDays, AlertHistoryLimit)
                });
                return response;
            }, GeneralConsts.AlertsNotRetrieved);

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
                    DefaultThresholdMs = _thresholds.CurrentValue.DefaultMs,
                    ThresholdOverrides = _thresholds.CurrentValue.Overrides,
                    AlertMetric = _alertOptions.CurrentValue.Metric,
                    AlertWindowMinutes = _alertOptions.CurrentValue.WindowMinutes,
                    NotificationsConfigured = _notifier.IsConfigured,
                    NotificationFormat = _notificationOptions.CurrentValue.Format
                });
                return Task.FromResult(response);
            });
    }
}

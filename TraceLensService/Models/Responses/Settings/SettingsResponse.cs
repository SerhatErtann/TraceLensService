namespace TraceLensService.Models.Responses.Settings
{
    /// <summary>Dashboard'un eşik çizgisi ve açıklamaları için.</summary>
    public class SettingsResponse
    {
        public double DefaultThresholdMs { get; set; }
        public Dictionary<string, double> ThresholdOverrides { get; set; } = [];
        public string AlertMetric { get; set; } = string.Empty;
        public int AlertWindowMinutes { get; set; }
        public bool NotificationsConfigured { get; set; }
        public string NotificationFormat { get; set; } = string.Empty;
    }
}

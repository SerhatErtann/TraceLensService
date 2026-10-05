namespace TraceLensService.Common
{
    public class GlobalConsts
    {
        public const string ConfigLine = "Config/appsettings.json";
        public const string DefaultCorsPolicy = "DefaultCorsPolicy";
        public const string SwaggerIsEnabled = "Swagger:IsEnabled";

        public const string ClickHouseConnection = "ClickHouse";
        public const string ClickHouseHttpClient = "clickhouse";
        public const string WebhookHttpClient = "webhook";
        public const int WebhookTimeoutSeconds = 10;

        // --- ClickHouse tabloları ---
        /// <summary>OTel Collector'ün ClickHouse exporter'ının oluşturduğu span tablosu.</summary>
        public const string TracesTable = "otel_traces";
        /// <summary>Alarm kayıtları; TraceLensService açılışta oluşturur (bkz. AlertQueries.EnsureSchemaAsync).</summary>
        public const string AlertsTable = "tracelens_alerts";
        public const int AlertRetentionDays = 90;

        // --- otel_traces alan değerleri (TraceLens.Instrumentation paketi ile birebir eşleşmeli) ---
        public const string ErrorStatus = "Error";
        public const string ServerSpanKind = "Server";
        public const string AppTypeAttribute = "tracelens.app_type";
        public const string JobNameAttribute = "job.name";
        public const string ServiceAppType = "service";
        public const string SchedulerAppType = "scheduler";

        // --- Sorgu limitleri ---
        public const int SummaryRowLimit = 1000;
        public const int MaxSpansPerTrace = 5000;
        public const int MaxRequestPageSize = 500;
        public const int DefaultRequestPageSize = 50;
        public const int AlertHistoryLimit = 200;
        public const int DefaultAlertHistoryDays = 7;

        // --- Bildirim formatları (Notifications:Format) ---
        public const string TeamsFormat = "teams";
        public const string SlackFormat = "slack";

        public class GeneralConsts
        {
            public const string SystemError = "Beklenmeyen bir hata oluştu.";
            public const string ServicesNotRetrieved = "Servis listesi alınamadı.";
            public const string SummaryNotRetrieved = "Operasyon özeti alınamadı.";
            public const string TotalsNotRetrieved = "Genel toplamlar alınamadı.";
            public const string TimeSeriesNotRetrieved = "Zaman serisi alınamadı.";
            public const string RequestsNotRetrieved = "İstek listesi alınamadı.";
            public const string TraceNotRetrieved = "Trace alınamadı.";
            public const string TraceNotFound = "Trace bulunamadı. Saklama süresi (7 gün) dolmuş olabilir.";
            public const string AlertsNotRetrieved = "Alarmlar alınamadı.";
            public const string NotificationNotConfigured = "Bildirim ayarlı değil (Notifications:WebhookUrl).";
            public const string NotificationSent = "Test bildirimi gönderildi.";

            // Swagger etiketleri
            public const string Services = "Services";
            public const string Schedulers = "Schedulers";
            public const string Traces = "Traces";
            public const string Alerts = "Alerts";
            public const string Settings = "Settings";
        }
    }
}

namespace TraceLensService.Common
{
    public class GlobalConsts
    {
        public const string ConfigLine = "Config/appsettings.json";
        public const string DefaultCorsPolicy = "DefaultCorsPolicy";
        public const string SwaggerIsEnabled = "Swagger:IsEnabled";
        public const string HealthUrl = "/health";

        // --- Giriş ---
        public const string AuthCookieName = "tracelens.auth";
        public const string LoginRateLimitPolicy = "login";
        /// <summary>Aynı IP'den dakikada izin verilen giriş denemesi.</summary>
        public const int LoginAttemptsPerMinute = 5;

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
        /// <summary>Eşikler; dashboard'dan yönetilir. İlk açılışta appsettings "Thresholds" bölümünden doldurulur.</summary>
        public const string ThresholdsTable = "tracelens_thresholds";

        // --- Eşik kuralları ---
        /// <summary>Varsayılan eşik satırının anahtarı (servis/operasyon boş).</summary>
        public const string DefaultThresholdKey = "*";
        public const double MinThresholdMs = 1;
        public const double MaxThresholdMs = 600_000;
        /// <summary>Birden fazla TraceLensService örneği çalışıyorsa diğerlerinin değişikliği bu sürede görülür.</summary>
        public const int ThresholdRefreshSeconds = 60;

        // --- otel_traces alan değerleri (servislerin OpenTelemetry kurulumu ile birebir eşleşmeli; bkz. README) ---
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

        // --- Genel Bakış / Sorunlar kuralları ---
        /// <summary>Bu orandan fazla hatalı istek varsa operasyon/servis "hatalı" sayılır.</summary>
        public const double IssueErrorRate = 0.05;
        /// <summary>Tek bir yavaş istek sorun sayılmasın: en az bu kadar istek olmalı.</summary>
        public const int IssueMinRequestCount = 3;
        /// <summary>Servis kartı grafiğindeki nokta sayısı.</summary>
        public const int OverviewTrendBuckets = 24;
        /// <summary>Genel Bakış'taki "en yavaş", "en çok hata veren" ve "son hatalar" listelerinin uzunluğu.</summary>
        public const int OverviewListSize = 5;

        // --- Servis Detayı ---
        /// <summary>Span grupları: servis içindeki metod (Internal span), DB sorgusu ve dış HTTP çağrısı.</summary>
        public const string SpanCategoryMethod = "method";
        public const string SpanCategoryDb = "db";
        public const string SpanCategoryCall = "call";
        public const int SpanGroupLimit = 300;
        public const int SpanSampleLimit = 10;
        /// <summary>Bir DB sorgusu istek başına bu kadar ya da daha çok çalışıyorsa N+1 şüphesi (trace ipucuyla aynı sınır).</summary>
        public const int NPlusOneCallsPerRequest = 10;
        /// <summary>Çağrılan servisin span'i aralığın biraz dışında kalabilir; eşleştirmede bu kadar pay bırakılır.</summary>
        public const int CalleeMatchMarginMs = 60_000;

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
            public const string OverviewNotRetrieved = "Genel bakış alınamadı.";
            public const string IssuesNotRetrieved = "Sorunlar alınamadı.";
            public const string BreakdownNotRetrieved = "Servis detayı alınamadı.";
            public const string SpanSamplesNotRetrieved = "En yavaş çağrılar alınamadı.";
            public const string ServiceRequired = "Servis adı zorunlu.";
            public const string InvalidSpanCategory = "Geçersiz tür (method, db veya call olmalı).";
            public const string NotificationNotConfigured = "Bildirim ayarlı değil (Notifications:WebhookUrl).";
            public const string NotificationSent = "Test bildirimi gönderildi.";
            public const string ThresholdsNotRetrieved = "Eşikler alınamadı.";
            public const string ThresholdNotSaved = "Eşik kaydedilemedi.";
            public const string ThresholdNotDeleted = "Eşik silinemedi.";
            public const string ThresholdOutOfRange = "Eşik 1 ms ile 600.000 ms arasında olmalı.";
            public const string ThresholdOperationRequired = "Servis ve operasyon adı zorunlu.";
            public const string ThresholdOverrideNotFound = "Bu operasyon için özel eşik tanımlı değil.";
            public const string ThresholdSaved = "Eşik kaydedildi.";
            public const string ThresholdDeleted = "Özel eşik kaldırıldı; varsayılan eşik geçerli.";
            public const string InvalidCredentials = "Kullanıcı adı veya şifre hatalı.";
            public const string LoginFailed = "Giriş yapılamadı.";
            public const string AuthDisabled = "Giriş kapalı (Auth:Password tanımlı değil).";
            public const string LoginRequired = "Oturum açmanız gerekiyor.";
            public const string TooManyLoginAttempts = "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.";
            public const string AuthPasswordMissing = "Auth:Required=true ama Auth:Password tanımlı değil. Şifreyi ortam değişkeniyle verin (Auth__Password / .env AUTH_PASSWORD).";

            // Swagger etiketleri
            public const string Auth = "Auth";
            public const string Overview = "Overview";
            public const string Thresholds = "Thresholds";
            public const string Services = "Services";
            public const string Schedulers = "Schedulers";
            public const string Traces = "Traces";
            public const string Alerts = "Alerts";
            public const string Settings = "Settings";
        }
    }
}

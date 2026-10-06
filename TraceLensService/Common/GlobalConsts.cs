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
        /// <summary>AI asistanı: soru başına API maliyeti olduğu için IP başına dakikalık sınır (Ai:QuestionsPerMinute).</summary>
        public const string AssistantRateLimitPolicy = "assistant";
        public const string AssistantPathPrefix = "api/v1/assistant";
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
        /// <summary>
        /// Raporlar için gün × endpoint/job özeti. Ham span'ler 7 gün tutulurken bu özet 90 gün tutulur; materialized view
        /// (DailyView) otel_traces'e her yazışta doldurur (bkz. ReportQueries.EnsureSchemaAsync).
        /// </summary>
        public const string DailyTable = "tracelens_daily";
        public const string DailyView = "tracelens_daily_mv";
        public const int DailyRetentionDays = 90;
        /// <summary>Eşikler; dashboard'dan yönetilir. İlk açılışta appsettings "Thresholds" bölümünden doldurulur.</summary>
        public const string ThresholdsTable = "tracelens_thresholds";
        /// <summary>Dashboard kullanıcıları (şifre özetiyle); bkz. UserQueries, UserStore.</summary>
        public const string UsersTable = "tracelens_users";
        public const int UserCacheSeconds = 30;
        /// <summary>Oturum cookie'sindeki şifre izi; şifre değişince eski oturumlar düşer.</summary>
        public const string PasswordStampClaim = "tl_pwd";
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 128;

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

        // --- Dağılımlar ---
        /// <summary>Süre dağılımı aralık sınırları (ms). Logaritmik: hem 2 ms'lik hem 20 s'lik istekler okunur kalır.</summary>
        public static readonly double[] HistogramEdgesMs =
            [1, 2, 3, 5, 7, 10, 15, 20, 30, 50, 70, 100, 150, 200, 300, 500, 700, 1000, 1500, 2000, 3000, 5000, 7000, 10000, 15000, 20000, 30000, 60000];
        public const int OutcomeErrorTypeLimit = 10;
        public const int InstanceLimit = 50;
        /// <summary>İstek anatomisi bu kadar son istekten çıkarılır (tüm span ağacı okunduğu için örneklem).</summary>
        public const int AnatomySampleSize = 300;

        // --- Canlı ---
        /// <summary>
        /// Span'ler servisten ~5 sn'de bir toplu çıkar, collector 2 sn'de bir yazar: bir istek bittikten birkaç saniye sonra
        /// görünür. Sayılar bu kadar geriden biten pencereyle hesaplanır.
        /// </summary>
        public const int LiveLagSeconds = 7;
        public const int LiveWindowSeconds = 60;
        /// <summary>İstek/sn bu kadar son saniyenin ortalaması.</summary>
        public const int LiveRateSeconds = 10;
        /// <summary>Geç yazılan (uzun süren ya da geç gönderilen) span'ler kaçmasın diye imleçten bu kadar geri bakılır.</summary>
        public const int LiveLookbackSeconds = 30;
        public const int LiveInitialRows = 50;
        public const int LiveMaxRows = 200;

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
            public const string OperationRequired = "Servis ve operasyon adı zorunlu.";
            public const string AnatomyNotRetrieved = "İstek anatomisi alınamadı.";
            public const string HistogramNotRetrieved = "Süre dağılımı alınamadı.";
            public const string OutcomesNotRetrieved = "Sonuç dağılımı alınamadı.";
            public const string InstancesNotRetrieved = "Instance listesi alınamadı.";
            public const string ServiceMapNotRetrieved = "Servis haritası alınamadı.";
            public const string LiveNotRetrieved = "Canlı veriler alınamadı.";
            public const string ReportNotRetrieved = "Rapor alınamadı.";
            public const string AssistantFailed = "Asistan şu an cevap veremedi.";
            public const string AssistantDisabled = "AI asistanı kapalı: TraceLensService'te ANTHROPIC_API_KEY (ya da Ai__ApiKey) ortam değişkeni tanımlı değil.";
            public const string AssistantEmptyQuestion = "Bir soru yazın.";
            public const string AssistantBadKey = "Claude API anahtarı geçersiz ya da yetkisiz.";
            public const string AssistantBusy = "Claude API şu an yoğun ya da kota doldu; biraz sonra tekrar deneyin.";
            public const string AssistantUnavailable = "Claude API şu an yanıt vermiyor; biraz sonra tekrar deneyin.";
            public const string AssistantRefused = "Bu soruya cevap veremiyorum. TraceLens verisiyle ilgili başka bir şey sorabilirsiniz.";
            public const string AssistantNoAnswer = "Cevap üretilemedi; soruyu farklı sorabilir misiniz?";
            public const string AssistantTooManySteps = "Soru çok fazla adım gerektirdi; daha dar bir soru deneyin (ör. tek bir servis ya da zaman aralığı).";
            public const string TooManyQuestions = "Çok sık soru soruldu. Bir dakika sonra tekrar deneyin.";
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
            public const string LoginRequired = "Oturum açmanız gerekiyor.";
            public const string TooManyLoginAttempts = "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.";
            public const string RegisterFailed = "Hesap oluşturulamadı.";
            public const string RegistrationClosed = "Kayıt kapalı. Hesabı olan biri sizi Ayarlar > Kullanıcılar'dan ekleyebilir.";
            public const string UsernameInvalid = "Kullanıcı adı 3-32 karakter olmalı; harf, rakam, nokta, alt çizgi ve tire kullanılabilir.";
            public const string PasswordInvalid = "Şifre en az 8, en fazla 128 karakter olmalı.";
            public const string UsernameTaken = "Bu kullanıcı adı alınmış.";
            public const string UsersNotRetrieved = "Kullanıcılar alınamadı.";
            public const string UserNotSaved = "Kullanıcı eklenemedi.";
            public const string UserNotDeleted = "Kullanıcı silinemedi.";
            public const string UserNotFound = "Kullanıcı bulunamadı.";
            public const string CannotDeleteSelf = "Kendi hesabınızı silemezsiniz.";
            public const string PasswordNotChanged = "Şifre değiştirilemedi.";
            public const string CurrentPasswordWrong = "Mevcut şifre hatalı.";

            // Swagger etiketleri
            public const string Auth = "Auth";
            public const string Overview = "Overview";
            public const string Thresholds = "Thresholds";
            public const string Services = "Services";
            public const string Schedulers = "Schedulers";
            public const string Traces = "Traces";
            public const string Alerts = "Alerts";
            public const string Settings = "Settings";
            public const string Assistant = "Assistant";
        }
    }
}

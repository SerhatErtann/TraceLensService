namespace TraceLensService.Common
{
    public class TraceLensRouteUrls
    {
        public const string ApiVersion = "api/v1/";

        // Giriş: bunlar dışındaki tüm uçlar oturum ister (şifre tanımlıysa)
        public const string AuthLogin = "auth/login";
        public const string AuthLogout = "auth/logout";
        public const string AuthStatus = "auth/me";

        // Services ve Schedulers sayfaları aynı uçları kullanır; yalnızca grup öneki değişir.
        public const string ServiceGroup = "api/v1/service";
        public const string SchedulerGroup = "api/v1/scheduler";

        public const string ServiceList = "/services";
        public const string Summary = "/summary";
        public const string Totals = "/totals";
        public const string TimeSeries = "/timeseries";
        public const string Requests = "/requests";

        // Servis Detayı: sürenin dağılımı + metod/DB/dış çağrı grupları, ve bir grubun en yavaş çağrıları
        public const string ServiceBreakdown = "/services/{service}/breakdown";
        public const string ServiceSpanSamples = "/services/{service}/spans";
        public const string ServiceAnatomy = "/services/{service}/anatomy";

        // Dağılımlar: ortak filtrelerle (service, operation, range ...)
        public const string Histogram = "/histogram";
        public const string Outcomes = "/outcomes";
        public const string Instances = "/instances";

        public const string ServiceMap = "service-map";
        public const string Live = "live";
        public const string Reports = "reports";

        public const string TraceDetail = "traces/{traceId}";

        // Genel Bakış ve Sorunlar: servis + scheduler birlikte
        public const string Overview = "overview";
        public const string Issues = "issues";

        public const string Alerts = "alerts";
        public const string AlertTestNotification = "alerts/test-notification";
        public const string Settings = "settings";

        // GET: varsayılan + özel eşikler, PUT: özel eşik ekle/güncelle, DELETE ?service&operation: özel eşiği kaldır
        public const string Thresholds = "thresholds";
        public const string ThresholdDefault = "thresholds/default";
    }
}

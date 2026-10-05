namespace TraceLensService.Common
{
    public class TraceLensRouteUrls
    {
        public const string ApiVersion = "api/v1/";

        // Services ve Schedulers sayfaları aynı uçları kullanır; yalnızca grup öneki değişir.
        public const string ServiceGroup = "api/v1/service";
        public const string SchedulerGroup = "api/v1/scheduler";

        public const string ServiceList = "/services";
        public const string Summary = "/summary";
        public const string Totals = "/totals";
        public const string TimeSeries = "/timeseries";
        public const string Requests = "/requests";

        public const string TraceDetail = "traces/{traceId}";

        public const string Alerts = "alerts";
        public const string AlertTestNotification = "alerts/test-notification";
        public const string Settings = "settings";
    }
}

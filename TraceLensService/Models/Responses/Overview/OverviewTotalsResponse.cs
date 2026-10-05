namespace TraceLensService.Models.Responses.Overview
{
    public class OverviewTotalsResponse
    {
        public int ServiceCount { get; set; }
        public int SchedulerCount { get; set; }
        public long RequestCount { get; set; }
        public double RequestsPerSecond { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }

        /// <summary>Süresi kendi operasyonunun eşiğini aşan istek/çalışma sayısı.</summary>
        public long SlowCount { get; set; }
        public double SlowRate { get; set; }
        public int OpenIssueCount { get; set; }
        public int ActiveAlertCount { get; set; }
    }
}

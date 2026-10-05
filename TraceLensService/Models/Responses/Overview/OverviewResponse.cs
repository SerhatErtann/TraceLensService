namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Genel Bakış sayfası: üstteki kutular + servis/scheduler kartları.</summary>
    public class OverviewResponse
    {
        public OverviewTotalsResponse Totals { get; set; } = new();
        public List<ServiceCardResponse> Services { get; set; } = [];

        /// <summary>Kart grafiklerinin zaman ekseni: Trend[i] = From + i * TrendBucketSeconds.</summary>
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int TrendBucketSeconds { get; set; }
    }
}

using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Genel Bakış sayfası: üstteki kutular + servis/scheduler kartları.</summary>
    public class OverviewResponse
    {
        public OverviewTotalsResponse Totals { get; set; } = new();
        public List<ServiceCardResponse> Services { get; set; } = [];

        /// <summary>Tüm uygulamaların yanıt süresi grafiği ve eşik çizgisi (varsayılan eşik).</summary>
        public List<TimeBucketResponse> Timeline { get; set; } = [];
        public double TimelineThresholdMs { get; set; }

        /// <summary>Ortalaması en yüksek 5 endpoint/job.</summary>
        public List<RankedOperationResponse> SlowestOperations { get; set; } = [];

        /// <summary>En çok hatalı isteği olan 5 endpoint/job.</summary>
        public List<RankedOperationResponse> MostErrors { get; set; } = [];

        /// <summary>En son hatalı istekler/çalışmalar.</summary>
        public List<RecentErrorResponse> RecentErrors { get; set; } = [];

        /// <summary>Kart grafiklerinin zaman ekseni: Trend[i] = From + i * TrendBucketSeconds.</summary>
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int TrendBucketSeconds { get; set; }
    }
}

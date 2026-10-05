using TraceLensService.Enums;
using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Models.Responses.Reports
{
    /// <summary>
    /// Bir dönemin raporu ve hemen önceki eşit uzunluktaki dönemle karşılaştırması. Günler <see cref="TimeZone"/>
    /// saat dilimine göre (gece yarısından gece yarısına).
    /// </summary>
    public class ReportResponse
    {
        /// <summary>today | yesterday | 7d | 30d | custom</summary>
        public string Period { get; set; } = string.Empty;
        /// <summary>Dönem bugünü içeriyor: bugün henüz bitmediği için istek sayısı önceki dönemden düşük görünebilir.</summary>
        public bool IncludesToday { get; set; }
        public string TimeZone { get; set; } = string.Empty;
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public DateOnly PreviousFrom { get; set; }
        public DateOnly PreviousTo { get; set; }
        /// <summary>Dönemin başı ve sonu (sonu hariç) UTC; tek günlük raporda saat saat grafik ham veriden bu aralıkla çekilir.</summary>
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public DateTime PreviousFromUtc { get; set; }
        /// <summary>Günlük özetin başladığı ilk gün; önceki dönem bundan eskiyse karşılaştırma eksik kalır.</summary>
        public DateOnly? DataSince { get; set; }

        public ReportTotalsResponse Totals { get; set; } = new();
        public ReportTotalsResponse PreviousTotals { get; set; } = new();

        /// <summary>Gün gün (gün başı, saat diliminde gece yarısı, UTC olarak). Veri olmayan günler yer almaz.</summary>
        public List<TimeBucketResponse> Daily { get; set; } = [];
        public List<TimeBucketResponse> PreviousDaily { get; set; } = [];

        /// <summary>Bu dönemde isteği olan tüm endpoint/job'lar, önceki dönem değerleriyle.</summary>
        public List<ReportOperationResponse> Operations { get; set; } = [];

        public ReportAlertsResponse Alerts { get; set; } = new();
    }

    public class ReportTotalsResponse
    {
        public long RequestCount { get; set; }
        public double AvgMs { get; set; }
        public double P50Ms { get; set; }
        public double P95Ms { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }
        /// <summary>Yaklaşık: süre dağılımı aralıklarından (bkz. ReportQueries.SlowCount).</summary>
        public long SlowCount { get; set; }
        public double SlowRate { get; set; }
        /// <summary>Verisi olan gün sayısı.</summary>
        public int DayCount { get; set; }
    }

    public class ReportOperationResponse
    {
        public AppKind App { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public double ThresholdMs { get; set; }
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long SlowCount { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }
        /// <summary>Önceki dönemde hiç isteği yoksa null.</summary>
        public long? PreviousCount { get; set; }
        public double? PreviousAvgMs { get; set; }
        public double? PreviousErrorRate { get; set; }
    }

    public class ReportAlertsResponse
    {
        public int Count { get; set; }
        /// <summary>Dönem içinde açık kaldıkları toplam süre (dakika).</summary>
        public double TotalMinutes { get; set; }
        /// <summary>Süresi en uzundan kısaya, en fazla 10.</summary>
        public List<ReportAlertResponse> Longest { get; set; } = [];
    }

    public class ReportAlertResponse
    {
        public AppKind App { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public double PeakValueMs { get; set; }
        public double ThresholdMs { get; set; }
        public DateTime FiredAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public double Minutes { get; set; }
    }
}

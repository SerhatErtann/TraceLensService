using TraceLensService.Enums;

namespace TraceLensService.Models.Responses.Issues
{
    /// <summary>Sorunlar sayfasındaki bir satır: eşiği aşan veya hata veren bir endpoint/job.</summary>
    public class IssueResponse
    {
        public AppKind App { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;

        /// <summary>error (hata oranı yüksek) | slow (ortalama eşiği aşıyor)</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Hem yavaş hem hatalıysa ikisi de true; Kind daha ağır olanı gösterir.</summary>
        public bool IsSlow { get; set; }
        public bool HasErrors { get; set; }

        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public double ThresholdMs { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }

        /// <summary>En sık görülen hata (ör. "HTTP 502" veya exception mesajı).</summary>
        public string? TopError { get; set; }

        public bool AlarmActive { get; set; }
        public DateTime? AlarmSince { get; set; }
        public DateTime LastSeen { get; set; }
    }
}

using TraceLensService.Enums;

namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Genel Bakış'taki bir servis/scheduler kartı.</summary>
    public class ServiceCardResponse
    {
        public string Service { get; set; } = string.Empty;
        public AppKind App { get; set; }

        /// <summary>ok | slow | error</summary>
        public string Status { get; set; } = string.Empty;
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }

        /// <summary>Ortalaması eşiğini aşan endpoint/job sayısı.</summary>
        public int SlowOperationCount { get; set; }

        /// <summary>Kart grafiğindeki eşik çizgisi (varsayılan eşik).</summary>
        public double ThresholdMs { get; set; }

        /// <summary>Kart grafiği: aralık eşit parçalara bölünmüş ortalama süreler (veri olmayan parça null).</summary>
        public List<double?> Trend { get; set; } = [];
    }
}

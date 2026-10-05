using TraceLensService.Enums;

namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Genel Bakış'taki "en yavaş" / "en çok hata veren" listelerinin bir satırı.</summary>
    public class RankedOperationResponse
    {
        public AppKind App { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public double ThresholdMs { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }
    }
}

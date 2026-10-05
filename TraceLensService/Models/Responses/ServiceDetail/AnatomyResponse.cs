namespace TraceLensService.Models.Responses.ServiceDetail
{
    /// <summary>
    /// Bir endpoint'in (veya job'un) "ortalama isteği": son N isteğin span ağaçlarından, bir istekte hangi adımın
    /// kaç kez çalıştığı ve ne kadar sürdüğü. Örn. "5 ms kendi kodu + payment-service çağrısı 1× 120 ms + DB 6× 0,7 ms".
    /// </summary>
    public class AnatomyResponse
    {
        public string Operation { get; set; } = string.Empty;
        /// <summary>İncelenen son istek sayısı (en fazla AnatomySampleSize).</summary>
        public int SampleCount { get; set; }
        public double AvgDurationMs { get; set; }
        /// <summary>Kendi kodu / dış çağrı / DB / diğer; Servis Detayı'ndaki çubukla aynı hesap, bu endpoint için.</summary>
        public List<TimeSplitResponse> TimeSplit { get; set; } = [];
        public List<AnatomyStepResponse> Steps { get; set; } = [];
    }

    public class AnatomyStepResponse
    {
        /// <summary>method | db | call</summary>
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        /// <summary>İstek başına ortalama kaç kez çalıştığı.</summary>
        public double CallsPerRequest { get; set; }
        /// <summary>İstek başına bu adımda geçen toplam süre (çağrılma × süre).</summary>
        public double MsPerRequest { get; set; }
        /// <summary>Bir çalışmasının ortalama süresi.</summary>
        public double AvgMs { get; set; }
        /// <summary>İstek süresine oranı (0–1). İç içe metodlar aynı süreyi paylaşabilir.</summary>
        public double Share { get; set; }
    }
}

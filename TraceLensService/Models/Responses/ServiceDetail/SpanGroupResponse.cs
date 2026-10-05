namespace TraceLensService.Models.Responses.ServiceDetail
{
    /// <summary>
    /// Servis içindeki aynı adlı span'lerin özeti: bir metod, bir DB sorgusu ("SELECT Orders")
    /// veya bir dış çağrı (çağrılan servisin route'u + servis adı).
    /// </summary>
    public class SpanGroupResponse
    {
        /// <summary>method | db | call</summary>
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Dış çağrıda çağrılan servis (span'i yoksa host:port); diğerlerinde boş.</summary>
        public string Target { get; set; } = string.Empty;
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public double MaxMs { get; set; }
        public long ErrorCount { get; set; }
        public double TotalMs { get; set; }
        public double ThresholdMs { get; set; }
        /// <summary>Servise gelen istek başına ortalama kaç kez çağrıldığı.</summary>
        public double CallsPerRequest { get; set; }
        /// <summary>İsteklerin toplam süresine oranı (0–1). İç içe span'ler aynı süreyi paylaşabildiği için toplamı 1'i geçebilir.</summary>
        public double Share { get; set; }
        /// <summary>DB sorgusu istek başına çok kez çalışıyor: döngü içinde sorgu (N+1) olabilir.</summary>
        public bool SuspectedNPlusOne { get; set; }

        public double ErrorRate => Count == 0 ? 0 : (double)ErrorCount / Count;
    }
}

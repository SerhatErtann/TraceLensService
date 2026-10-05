namespace TraceLensService.Models.Responses.Analysis
{
    /// <summary>
    /// Süre dağılımı: istekler logaritmik aralıklara (1, 2, 3, 5, 7, 10 ms ...) dağıtılır. Ortalamanın gizlediğini gösterir:
    /// "hepsi biraz yavaş" mı, yoksa "çoğu hızlı ama bir grup çok yavaş" mı.
    /// </summary>
    public class HistogramResponse
    {
        public long Count { get; set; }
        public double P50Ms { get; set; }
        public double P90Ms { get; set; }
        public double P99Ms { get; set; }
        /// <summary>Sadece istek olan aralıklar değil, ilk ve son dolu aralık arasındaki tüm aralıklar (boşluklar 0).</summary>
        public List<HistogramBucketResponse> Buckets { get; set; } = [];
    }

    public class HistogramBucketResponse
    {
        public double FromMs { get; set; }
        /// <summary>Son aralıkta null (üst sınır yok).</summary>
        public double? ToMs { get; set; }
        public long Count { get; set; }
        public long ErrorCount { get; set; }
    }
}

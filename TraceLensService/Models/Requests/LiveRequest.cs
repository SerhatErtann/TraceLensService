namespace TraceLensService.Models.Requests
{
    /// <summary>
    /// Canlı akış sorgusu. <see cref="Since"/> boşsa son istekler, doluysa o andan (biraz geriden) sonrakiler gelir;
    /// dashboard aynı span'i iki kez almamak için SpanId ile ayıklar.
    /// </summary>
    public class LiveRequest
    {
        public DateTimeOffset? Since { get; set; }
        /// <summary>service | scheduler; boşsa ikisi birden.</summary>
        public string? App { get; set; }
        public string? Service { get; set; }
        public string? Operation { get; set; }
        /// <summary>Sadece listeyi filtreler; üstteki sayılar tüm istekler üzerinden.</summary>
        public bool? OnlySlow { get; set; }
        public bool? OnlyErrors { get; set; }
    }
}

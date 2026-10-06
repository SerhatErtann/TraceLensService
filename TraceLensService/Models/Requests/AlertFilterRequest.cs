namespace TraceLensService.Models.Requests
{
    /// <summary>
    /// Alarmlar sayfasının filtreleri. Zaman aralığı ya <see cref="Days"/> (son N gün) ya da <see cref="From"/>/<see cref="To"/>;
    /// o aralıkta açık kalmış her alarm (aralıkta açılan ya da süren) gelir. Diğer filtreler boşsa uygulanmaz.
    /// </summary>
    public class AlertFilterRequest
    {
        public int? Days { get; set; }
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        /// <summary>service | scheduler</summary>
        public string? App { get; set; }
        public string? Service { get; set; }
        /// <summary>Operasyon adında geçen metin (büyük/küçük harf duyarsız).</summary>
        public string? Operation { get; set; }
        /// <summary>slow (yavaşlık) | error (hata)</summary>
        public string? Kind { get; set; }
        /// <summary>En yüksek süre (ms) en az bu kadar olanlar.</summary>
        public double? MinPeakMs { get; set; }
        /// <summary>En sık hata kodu: "500" gibi tam kod ya da "5xx" / "4xx" sınıfı.</summary>
        public string? Status { get; set; }
    }
}

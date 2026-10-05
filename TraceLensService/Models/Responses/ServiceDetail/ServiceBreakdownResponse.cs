namespace TraceLensService.Models.Responses.ServiceDetail
{
    /// <summary>
    /// Servis Detayı: sürenin nereye gittiği ve servisin içindeki metod / DB sorgusu / dış çağrı grupları.
    /// Endpoint'ler (veya job'lar) ayrıca summary ucundan gelir.
    /// </summary>
    public class ServiceBreakdownResponse
    {
        public string Service { get; set; } = string.Empty;
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        /// <summary>Servise gelen istek (veya job çalışması) sayısı.</summary>
        public long RequestCount { get; set; }
        /// <summary>Bu isteklerin toplam süresi; tablolardaki "toplam sürenin payı" buna göre hesaplanır.</summary>
        public double RequestTotalMs { get; set; }

        /// <summary>Kendi kodu / dış çağrılar / veritabanı / diğer: toplamı %100.</summary>
        public List<TimeSplitResponse> TimeSplit { get; set; } = [];

        public List<SpanGroupResponse> Methods { get; set; } = [];
        public List<SpanGroupResponse> Database { get; set; } = [];
        public List<SpanGroupResponse> Calls { get; set; } = [];
    }
}

namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Servis haritası: kim kimi çağırıyor, kenarlarda çağrı sayısı, süre ve hata.</summary>
    public class ServiceMapResponse
    {
        public List<ServiceMapNodeResponse> Nodes { get; set; } = [];
        public List<ServiceMapEdgeResponse> Edges { get; set; } = [];
    }

    public class ServiceMapNodeResponse
    {
        /// <summary>Servislerde servis adı; veritabanında "db:sqlite · main"; span'i olmayan hedefte "ext:host:port".</summary>
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>service | scheduler | database | external</summary>
        public string Kind { get; set; } = string.Empty;
        /// <summary>Servis/görevde gelen istek (çalışma); veritabanı ve dış hedefte gelen çağrı sayısı.</summary>
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double ErrorRate { get; set; }
        /// <summary>ok | slow | error (Genel Bakış kartıyla aynı kural)</summary>
        public string Status { get; set; } = "ok";
    }

    public class ServiceMapEdgeResponse
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public long Count { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate => Count == 0 ? 0 : (double)ErrorCount / Count;
    }
}

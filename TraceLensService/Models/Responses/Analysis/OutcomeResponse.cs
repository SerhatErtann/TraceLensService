namespace TraceLensService.Models.Responses.Analysis
{
    /// <summary>İsteklerin sonucu: HTTP durum kodları (görevlerde başarılı/başarısız) ve hataların türleri.</summary>
    public class OutcomeResponse
    {
        public long Count { get; set; }
        public List<StatusCountResponse> Statuses { get; set; } = [];
        public List<ErrorTypeResponse> ErrorTypes { get; set; } = [];
    }

    public class StatusCountResponse
    {
        /// <summary>"200", "502" ...; görevlerde "succeeded" / "failed"; bilinmiyorsa boş.</summary>
        public string Status { get; set; } = string.Empty;
        public long Count { get; set; }
        public long ErrorCount { get; set; }
    }

    public class ErrorTypeResponse
    {
        /// <summary>Exception tipi (error.type / exception.type), yoksa "HTTP 502" veya hata mesajı.</summary>
        public string Type { get; set; } = string.Empty;
        public long Count { get; set; }
        public string? ExampleMessage { get; set; }
        /// <summary>Bu hata en çok hangi operasyonda.</summary>
        public string TopOperation { get; set; } = string.Empty;
        public string LastTraceId { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
    }
}

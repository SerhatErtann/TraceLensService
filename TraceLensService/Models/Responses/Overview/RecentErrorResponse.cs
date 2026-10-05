using TraceLensService.Enums;

namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Genel Bakış'taki "Son hatalar" listesinin bir satırı.</summary>
    public class RecentErrorResponse
    {
        public AppKind App { get; set; }
        public DateTime Timestamp { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public double DurationMs { get; set; }

        /// <summary>Hata özeti: exception mesajı ya da "HTTP 502".</summary>
        public string Error { get; set; } = string.Empty;
    }
}

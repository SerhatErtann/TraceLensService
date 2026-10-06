using TraceLensService.Models.DbModels;

namespace TraceLensService.Models.Responses.Alerts
{
    public class AlertListResponse
    {
        /// <summary>Filtrelenen zaman aralığı.</summary>
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        /// <summary>Şu an açık alarmlar (filtreler uygulanmış).</summary>
        public List<AlertRecord> Active { get; set; } = [];
        /// <summary>Aralıkta kapanmış alarmlar (filtreler uygulanmış), en yeni önce.</summary>
        public List<AlertRecord> History { get; set; } = [];

        /// <summary>Filtre seçenekleri: aralıktaki alarmlarda geçen uygulamalar ve hata kodları (filtrelerden önce).</summary>
        public List<string> Services { get; set; } = [];
        public List<string> Statuses { get; set; } = [];
    }
}

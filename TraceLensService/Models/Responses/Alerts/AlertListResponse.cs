using TraceLensService.Models.DbModels;

namespace TraceLensService.Models.Responses.Alerts
{
    public class AlertListResponse
    {
        public List<AlertRecord> Active { get; set; } = [];
        public List<AlertRecord> History { get; set; } = [];
    }
}

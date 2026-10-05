namespace TraceLensService.Models.Responses.Thresholds
{
    public class ThresholdListResponse
    {
        public double DefaultMs { get; set; }
        public List<ThresholdOverrideResponse> Overrides { get; set; } = [];
    }
}

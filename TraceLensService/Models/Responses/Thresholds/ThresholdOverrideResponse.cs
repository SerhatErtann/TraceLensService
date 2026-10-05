namespace TraceLensService.Models.Responses.Thresholds
{
    public class ThresholdOverrideResponse
    {
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public double ThresholdMs { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

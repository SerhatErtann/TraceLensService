namespace TraceLensService.Models.Requests
{
    /// <summary>Bir operasyon için özel eşik ekler veya günceller.</summary>
    public class ThresholdRequest
    {
        public string Service { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public double ThresholdMs { get; set; }
    }
}

namespace TraceLensService.Models.Requests
{
    /// <summary>Özel eşiği olmayan tüm operasyonlar için geçerli varsayılan eşik.</summary>
    public class DefaultThresholdRequest
    {
        public double ThresholdMs { get; set; }
    }
}

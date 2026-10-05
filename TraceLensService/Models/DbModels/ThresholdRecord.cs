namespace TraceLensService.Models.DbModels
{
    /// <summary>
    /// tracelens_thresholds tablosundaki bir eşik. Varsayılan eşik <c>Service</c> ve <c>Operation</c> boş olan satırdır.
    /// </summary>
    public sealed record ThresholdRecord(string Service, string Operation, double ThresholdMs, DateTime UpdatedAt)
    {
        public bool IsDefault => Service.Length == 0 && Operation.Length == 0;
    }
}

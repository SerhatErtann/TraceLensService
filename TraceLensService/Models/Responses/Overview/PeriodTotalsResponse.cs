namespace TraceLensService.Models.Responses.Overview
{
    /// <summary>Bir önceki eşit uzunluktaki dönemin toplamları; kutulardaki "önceki döneme göre" karşılaştırması için.</summary>
    public class PeriodTotalsResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public long RequestCount { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }
        public long SlowCount { get; set; }
        public double SlowRate { get; set; }
    }
}

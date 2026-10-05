namespace TraceLensService.Models.Options
{
    public class AlertOptions
    {
        public const string SectionName = "Alerts";

        public bool Enabled { get; set; } = true;
        public int CheckIntervalSeconds { get; set; } = 60;
        public int WindowMinutes { get; set; } = 5;

        /// <summary>Bu kadar istek gelmeden alarm üretilmez (tek bir yavaş istek alarm sayılmasın).</summary>
        public int MinRequestCount { get; set; } = 5;

        /// <summary>"avg" (ortalama) veya "p95".</summary>
        public string Metric { get; set; } = "avg";

        /// <summary>
        /// Açık bir alarm, değer eşiğin bu oranının altına inince kapanır (0.9 = %90).
        /// Eşik etrafında gidip gelen değerlerin sürekli aç/kapa bildirimi üretmesini engeller.
        /// </summary>
        public double ResolveRatio { get; set; } = 0.9;
    }
}

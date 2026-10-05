namespace TraceLensService.Models.Options
{
    public class ReportOptions
    {
        public const string SectionName = "Reports";

        /// <summary>
        /// Raporlardaki "gün" bu saat dilimine göre (gece yarısından gece yarısına). Günlük özet tablosu ilk kurulurken
        /// sabitlenir; sonradan değiştirilirse tracelens_daily ve tracelens_daily_mv silinip yeniden oluşturulmalı.
        /// </summary>
        public string TimeZone { get; set; } = "Europe/Istanbul";
    }
}

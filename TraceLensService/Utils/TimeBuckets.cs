namespace TraceLensService.Utils
{
    public static class TimeBuckets
    {
        /// <summary>Zaman aralığına göre grafik çözünürlüğü (saniye).</summary>
        public static int For(TimeSpan range) => range.TotalMinutes switch
        {
            <= 15 => 15,
            <= 60 => 60,
            <= 360 => 300,
            <= 1440 => 900,
            _ => 3600
        };
    }
}

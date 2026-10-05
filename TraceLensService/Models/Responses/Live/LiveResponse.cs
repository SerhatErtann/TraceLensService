using TraceLensService.Enums;
using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Models.Responses.Live
{
    public class LiveResponse
    {
        /// <summary>Bir sonraki sorguda Since olarak gönderilecek değer (dönen en yeni isteğin zamanı).</summary>
        public DateTime? Cursor { get; set; }
        /// <summary>En yeni üstte.</summary>
        public List<LiveRowResponse> Items { get; set; } = [];
        public LiveStatsResponse Stats { get; set; } = new();
    }

    public class LiveRowResponse : RequestRowResponse
    {
        public AppKind App { get; set; }
    }

    /// <summary>
    /// Son 60 saniyenin özeti. Veri servislerden toplu geldiği için en son <see cref="LagSeconds"/> saniye eksik olur;
    /// pencere o kadar geriden biter ki sayılar yarım saniyelerle düşük görünmesin.
    /// </summary>
    public class LiveStatsResponse
    {
        public int WindowSeconds { get; set; }
        public int LagSeconds { get; set; }
        public DateTime WindowEnd { get; set; }
        public long Count { get; set; }
        /// <summary>Son 10 saniyenin ortalaması.</summary>
        public double RequestsPerSecond { get; set; }
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public long SlowCount { get; set; }
        public long ErrorCount { get; set; }
        /// <summary>En çok hata veren servis ve hata sayısı (hata yoksa null).</summary>
        public string? TopErrorService { get; set; }
        public long TopErrorServiceCount { get; set; }
        /// <summary>Saniye başına: tam <see cref="WindowSeconds"/> eleman, eskiden yeniye.</summary>
        public List<LiveSecondResponse> Seconds { get; set; } = [];
    }

    public class LiveSecondResponse
    {
        public DateTime Time { get; set; }
        public long Count { get; set; }
        public long SlowCount { get; set; }
        public long ErrorCount { get; set; }
    }
}

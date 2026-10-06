namespace TraceLensService.Models.Options
{
    /// <summary>
    /// AI asistanı (Claude API). Anahtar dosyaya yazılmaz: ortam değişkeni <c>ANTHROPIC_API_KEY</c> ya da <c>Ai__ApiKey</c>.
    /// Anahtar yoksa asistan kapalıdır; dashboard bunu söyler.
    /// </summary>
    public class AiOptions
    {
        public const string SectionName = "Ai";

        public string? ApiKey { get; set; }
        public string Model { get; set; } = "claude-opus-5-5";

        /// <summary>Bir soruda Claude'un en fazla kaç tur araç çağırabileceği (sonsuz döngüye karşı).</summary>
        public int MaxToolRounds { get; set; } = 8;

        /// <summary>Kullanıcı (IP) başına dakikada soru sınırı; her soru API maliyeti demek.</summary>
        public int QuestionsPerMinute { get; set; } = 10;
    }
}

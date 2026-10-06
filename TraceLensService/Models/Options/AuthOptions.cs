namespace TraceLensService.Models.Options
{
    /// <summary>
    /// Dashboard girişi her zaman açıktır; kullanıcılar ClickHouse'taki tracelens_users tablosunda tutulur.
    /// Hiç kullanıcı yokken giriş sayfası "ilk hesabı oluştur" ekranı açar.
    /// </summary>
    public class AuthOptions
    {
        public const string SectionName = "Auth";

        /// <summary>
        /// Opsiyonel ilk hesap (sunucu kurulumu için): hiç kullanıcı yoksa bu kullanıcı adı/şifreyle oluşturulur.
        /// Dosyaya değil ortam değişkenine yazılır: Auth__Username, Auth__Password (.env: AUTH_USERNAME, AUTH_PASSWORD).
        /// </summary>
        public string Username { get; set; } = "admin";
        public string? Password { get; set; }

        /// <summary>
        /// true: giriş sayfasında herkes kendine hesap açabilir (yerel/ekip içi kullanım).
        /// false: ilk hesaptan sonra yeni kullanıcıları yalnızca giriş yapmış biri Ayarlar'dan ekler (sunucu için önerilen).
        /// </summary>
        public bool AllowRegistration { get; set; }

        public int SessionHours { get; set; } = 8;
    }
}

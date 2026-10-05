namespace TraceLensService.Models.Options
{
    /// <summary>
    /// Dashboard girişi. <see cref="Password"/> doluysa giriş açıktır; boşsa (yerel geliştirme) herkes erişir.
    /// Kullanıcı adı ve şifre dosyaya değil ortam değişkenine yazılır: Auth__Username, Auth__Password.
    /// </summary>
    public class AuthOptions
    {
        public const string SectionName = "Auth";

        public string Username { get; set; } = "admin";
        public string? Password { get; set; }

        /// <summary>true ise şifre tanımlı değilken uygulama açılmaz (sunucuda yanlışlıkla korumasız yayını engeller).</summary>
        public bool Required { get; set; }

        public int SessionHours { get; set; } = 8;

        public bool Enabled => !string.IsNullOrEmpty(Password);
    }
}

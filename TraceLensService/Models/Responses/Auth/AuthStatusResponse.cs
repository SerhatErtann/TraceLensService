namespace TraceLensService.Models.Responses.Auth
{
    public class AuthStatusResponse
    {
        public bool Authenticated { get; set; }
        public string? Username { get; set; }
        /// <summary>Hiç kullanıcı yok: giriş sayfası "ilk hesabı oluştur" ekranını gösterir.</summary>
        public bool SetupRequired { get; set; }
        /// <summary>Giriş sayfasında "Kayıt ol" seçeneği var mı (Auth:AllowRegistration).</summary>
        public bool RegistrationOpen { get; set; }
    }
}

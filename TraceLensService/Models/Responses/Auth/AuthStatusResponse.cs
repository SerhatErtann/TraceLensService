namespace TraceLensService.Models.Responses.Auth
{
    public class AuthStatusResponse
    {
        /// <summary>false ise giriş kapalıdır (şifre tanımlı değil); dashboard giriş sayfası göstermez.</summary>
        public bool AuthEnabled { get; set; }
        public bool Authenticated { get; set; }
        public string? Username { get; set; }
    }
}

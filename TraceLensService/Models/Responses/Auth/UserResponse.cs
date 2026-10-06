namespace TraceLensService.Models.Responses.Auth
{
    public class UserResponse
    {
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        /// <summary>Listeyi isteyen kullanıcının kendisi (silinemez).</summary>
        public bool IsMe { get; set; }
    }
}

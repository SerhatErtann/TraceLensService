namespace TraceLensService.Models.Requests
{
    /// <summary>Sohbetin şimdiye kadarki metni; son mesaj kullanıcının yeni sorusu. Sunucu sohbet saklamaz.</summary>
    public class AssistantRequest
    {
        public List<AssistantMessage> Messages { get; set; } = [];
    }

    public class AssistantMessage
    {
        /// <summary>user | assistant</summary>
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }
}

namespace TraceLensService.Models.Responses.Assistant
{
    public class AssistantResponse
    {
        /// <summary>Markdown (kalın, liste, /services/... gibi dashboard içi linkler).</summary>
        public string Answer { get; set; } = string.Empty;
        /// <summary>Cevap için bakılan veriler, ekranda "Sorunlar incelendi" gibi gösterilir.</summary>
        public List<string> Steps { get; set; } = [];
        public string Model { get; set; } = string.Empty;
    }

    public class AssistantStatusResponse
    {
        public bool Enabled { get; set; }
        public string Model { get; set; } = string.Empty;
    }
}

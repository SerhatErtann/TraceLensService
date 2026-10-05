namespace TraceLensService.Models.Options
{
    public class NotificationOptions
    {
        public const string SectionName = "Notifications";

        /// <summary>Boşsa bildirim gönderilmez. Gizli bilgidir; ortam değişkeninden verin (Notifications__WebhookUrl).</summary>
        public string? WebhookUrl { get; set; }

        /// <summary>
        /// "teams" (Teams Workflows "webhook isteği alındığında kanala gönder" akışı, Adaptive Card),
        /// "slack" (Incoming Webhook) veya "generic" (ham JSON).
        /// </summary>
        public string Format { get; set; } = "generic";

        /// <summary>Bildirimdeki "Dashboard'da aç" linki için, örn. https://tracelens.finoku.local</summary>
        public string? DashboardUrl { get; set; }
    }
}

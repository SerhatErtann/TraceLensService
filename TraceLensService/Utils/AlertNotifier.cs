using Microsoft.Extensions.Options;
using TraceLensService.Common;
using TraceLensService.Enums;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Options;

namespace TraceLensService.Utils
{
    /// <summary>Alarm açıldığında/kapandığında webhook'a seçilen formatta (teams, slack, generic) mesaj gönderir.</summary>
    public class AlertNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<NotificationOptions> options,
        IOptionsMonitor<AlertOptions> alertOptions,
        ILogger<AlertNotifier> logger)
    {
        public const string Fired = "fired";
        public const string Resolved = "resolved";
        public const string Test = "test";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.WebhookUrl);

        /// <summary>Gönderir; hata olursa loglar ve mesajını döner (alarm akışını durdurmaz). Başarılıysa null.</summary>
        public async Task<string?> NotifyAsync(string state, AlertRecord alert, CancellationToken ct = default)
        {
            NotificationOptions opts = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(opts.WebhookUrl))
                return GlobalConsts.GeneralConsts.NotificationNotConfigured;

            string title = BuildTitle(state, alert);
            string? link = BuildLink(opts.DashboardUrl, alert);
            object payload = opts.Format.ToLowerInvariant() switch
            {
                GlobalConsts.TeamsFormat => TeamsCard(state, title, alert, link),
                GlobalConsts.SlackFormat => new { text = link is null ? title : $"{title}\n<{link}|Dashboard'da aç>" },
                _ => new { text = title, state, alert, link }
            };

            try
            {
                using HttpResponseMessage response = await httpClientFactory
                    .CreateClient(GlobalConsts.WebhookHttpClient)
                    .PostAsJsonAsync(opts.WebhookUrl, payload, ct);
                if (response.IsSuccessStatusCode) return null;

                string body = await response.Content.ReadAsStringAsync(ct);
                string error = $"Webhook {(int)response.StatusCode} döndü: {Truncate(body, 300)}";
                logger.LogWarning("Alarm bildirimi gönderilemedi: {Error}", error);
                return error;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Alarm bildirimi gönderilemedi");
                return ex.Message;
            }
        }

        private static string BuildTitle(string state, AlertRecord a) => state switch
        {
            Fired => $"⚠️ {a.Service} · {a.Operation}: {a.Metric} {a.ValueMs:0} ms (eşik {a.ThresholdMs:0} ms)",
            Resolved => $"✅ {a.Service} · {a.Operation} normale döndü ({a.DurationMinutes:0.#} dk sürdü, en yüksek {a.PeakValueMs:0} ms)",
            _ => "🔔 TraceLens test bildirimi: webhook çalışıyor"
        };

        private object TeamsCard(string state, string title, AlertRecord a, string? link)
        {
            List<object> facts =
            [
                new { title = "Servis", value = a.Service },
                new { title = "Operasyon", value = a.Operation },
                new { title = a.Metric == "p95" ? "p95" : "Ortalama", value = $"{a.ValueMs:0} ms" },
                new { title = "Eşik", value = $"{a.ThresholdMs:0} ms" },
                new { title = "İstek / eşiği aşan", value = $"{a.RequestCount} / {a.SlowCount} (son {alertOptions.CurrentValue.WindowMinutes} dk)" }
            ];
            if (state == Resolved)
                facts.Add(new { title = "Süre", value = $"{a.DurationMinutes:0.#} dk (en yüksek {a.PeakValueMs:0} ms)" });

            Dictionary<string, object> card = new()
            {
                ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                ["type"] = "AdaptiveCard",
                ["version"] = "1.4",
                ["body"] = new object[]
                {
                    new { type = "TextBlock", text = title, weight = "Bolder", wrap = true,
                          color = state == Fired ? "Attention" : state == Resolved ? "Good" : "Default" },
                    new { type = "FactSet", facts }
                },
                ["actions"] = link is null
                    ? Array.Empty<object>()
                    : new object[] { new { type = "Action.OpenUrl", title = "Dashboard'da aç", url = link } }
            };

            return new
            {
                type = "message",
                attachments = new[] { new { contentType = "application/vnd.microsoft.card.adaptive", content = card } }
            };
        }

        private static string? BuildLink(string? dashboardUrl, AlertRecord a)
        {
            if (string.IsNullOrWhiteSpace(dashboardUrl)) return null;
            string page = a.App == AppKind.Service ? "services" : "schedulers";
            return $"{dashboardUrl.TrimEnd('/')}/{page}?range=15m" +
                   $"&service={Uri.EscapeDataString(a.Service)}&operation={Uri.EscapeDataString(a.Operation)}";
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
    }
}

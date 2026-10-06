using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using CommonUtils.Models;
using Microsoft.Extensions.Options;
using TraceLensService.Models.Options;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Assistant;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>
    /// AI asistanı: soruyu Claude'a sorar, Claude'un istediği araçları (AssistantTools) çalıştırıp sonucu geri verir,
    /// Claude yeterli veriyi toplayınca Türkçe cevabı döner. Sohbet sunucuda saklanmaz; istemci önceki metinleri gönderir.
    /// </summary>
    public class AssistantBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<AssistantBusiness>>())
    {
        private const int MaxHistoryMessages = 12;
        private const int MaxMessageChars = 4000;

        private readonly IOptionsMonitor<AiOptions> _options = serviceProvider.GetRequiredService<IOptionsMonitor<AiOptions>>();
        private readonly AssistantTools _tools = serviceProvider.GetRequiredService<AssistantTools>();

        // Sabit kalır (önbelleğe alınabilsin diye tarih/saat gibi değişen bilgi buraya değil, soruya eklenir)
        private const string SystemPrompt = """
            Sen TraceLens'in asistanısın. TraceLens; servislerin (HTTP istekleri) ve zamanlanmış görevlerin (scheduler) ne kadar
            sürdüğünü, hangilerinin eşiği aştığını ve hata verdiğini OpenTelemetry verisinden gösteren bir izleme panelidir.
            Kullanıcı bu paneli kullanan bir geliştirici; Türkçe soruyor.

            Nasıl çalışırsın:
            - Sayı, servis adı ya da durum içeren her soruda önce araçlarla veriye bak; sayıları asla tahmin etme ya da uydurma.
              Araçlar dashboard'daki sayıların aynısını verir.
            - Soru belirsizse makul bir varsayım yap (ör. "bugün" = Türkiye saatiyle bugün gece yarısından şimdiye) ve cevapta
              bu varsayımı tek cümleyle söyle. Gerekirse birden çok aracı birlikte çağır.
            - Ham istek verisi 7 gün tutulur; daha eski dönemler için get_report (90 gün) kullan.
            - Veri yoksa ya da araç hata verirse bunu açıkça söyle.

            Cevap biçimi:
            - Türkçe, kısa ve net. İlk cümle sorunun cevabı olsun; sonra en fazla birkaç madde.
            - Sayıları birimiyle ver (ms, s, %). p95 gibi terimleri ilk geçtiği yerde kısaca açıkla
              (p95: isteklerin %95'i bu süreden kısa).
            - "Eşik": o endpoint/görev için yavaş sayılma sınırı. "Hatalı": hata durum kodu (5xx) ya da exception ile biten istek.
            - Markdown kullan: **kalın**, madde listesi. Tablo kullanma.
            - Kullanıcıyı ilgili dashboard sayfasına markdown link ile yönlendir (sadece bu yollar, göreli adres):
              /overview (Genel Bakış), /issues (Sorunlar), /services?service=<ad> (servis istekleri),
              /schedulers?service=<ad> (görev çalışmaları), /services/<ad> ya da /schedulers/<ad> (uygulama detayı),
              /alerts (Alarmlar), /reports?period=<today|yesterday|7d|30d> (Raporlar), /traces/<traceId> (tek istek dökümü).
              Zaman aralığı için linke &range=1h|6h|24h|7d ekleyebilirsin.
            - Konu TraceLens verisi dışındaysa kısaca bunun panelin kapsamı dışında olduğunu söyle.
            """;

        public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);
        private string? ApiKey => _options.CurrentValue.ApiKey is { Length: > 0 } key ? key : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        public Task<DataResponse<AssistantStatusResponse>> GetStatus()
            => Guard(() =>
            {
                DataResponse<AssistantStatusResponse> response = new();
                response.Success(new AssistantStatusResponse { Enabled = IsEnabled, Model = _options.CurrentValue.Model });
                return Task.FromResult(response);
            });

        public Task<DataResponse<AssistantResponse>> Ask(AssistantRequest request, CancellationToken ct)
            => Guard(async () =>
            {
                DataResponse<AssistantResponse> response = new();
                if (!IsEnabled)
                    throw new FriendlyException(GeneralConsts.AssistantDisabled);

                List<BetaMessageParam> messages = BuildHistory(request);
                if (messages.Count == 0)
                    throw new FriendlyException(GeneralConsts.AssistantEmptyQuestion);

                AiOptions opts = _options.CurrentValue;
                AnthropicClient client = new() { ApiKey = ApiKey };
                List<string> steps = [];

                for (int round = 0; round < opts.MaxToolRounds; round++)
                {
                    BetaMessage reply = await Send(client, opts, messages, ct);

                    if (reply.StopReason == "refusal")
                    {
                        response.Success(new AssistantResponse { Answer = GeneralConsts.AssistantRefused, Steps = steps, Model = opts.Model });
                        return response;
                    }

                    // Claude'un cevabı (düşünme blokları dahil, değiştirilmeden) sohbete eklenir; araç istediyse çalıştırılıp
                    // sonuçları tek bir kullanıcı mesajında geri verilir
                    (List<BetaContentBlockParam> echo, List<BetaToolUseBlock> toolCalls, string text) = Read(reply);
                    messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = echo });

                    if (reply.StopReason != "tool_use" || toolCalls.Count == 0)
                    {
                        response.Success(new AssistantResponse
                        {
                            Answer = text.Length > 0 ? text : GeneralConsts.AssistantNoAnswer,
                            Steps = steps,
                            Model = opts.Model
                        });
                        return response;
                    }

                    List<BetaContentBlockParam> results = [];
                    foreach (BetaToolUseBlock call in toolCalls)
                    {
                        (string content, bool isError) = await _tools.ExecuteAsync(call.Name, call.Input);
                        _logger.LogInformation("Asistan aracı {Tool} {Input} → {Length} karakter{Error}",
                            call.Name, JsonSerializer.Serialize(call.Input), content.Length, isError ? " (hata)" : string.Empty);
                        if (AssistantTools.Labels.TryGetValue(call.Name, out string? label) && !steps.Contains(label))
                            steps.Add(label);
                        results.Add(new BetaToolResultBlockParam { ToolUseID = call.ID, Content = content, IsError = isError });
                    }
                    messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
                }

                response.Success(new AssistantResponse { Answer = GeneralConsts.AssistantTooManySteps, Steps = steps, Model = opts.Model });
                return response;
            }, GeneralConsts.AssistantFailed);

        private static async Task<BetaMessage> Send(AnthropicClient client, AiOptions opts, List<BetaMessageParam> messages, CancellationToken ct)
        {
            try
            {
                return await client.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = opts.Model,
                    MaxTokens = 16000,
                    // Güvenlik filtresi soruyu reddederse sunucu tarafında başka bir modele düşülür (aynı çağrı içinde)
                    Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                    Fallbacks = new Default(),
                    OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                    System = new List<BetaTextBlockParam>
                    {
                        new() { Text = SystemPrompt, CacheControl = new BetaCacheControlEphemeral() }
                    },
                    Tools = ToolDefinitions,
                    Messages = messages
                }, ct);
            }
            catch (AnthropicUnauthorizedException)
            {
                throw new FriendlyException(GeneralConsts.AssistantBadKey);
            }
            catch (AnthropicRateLimitException)
            {
                throw new FriendlyException(GeneralConsts.AssistantBusy);
            }
            catch (Anthropic5xxException)
            {
                throw new FriendlyException(GeneralConsts.AssistantUnavailable);
            }
        }

        private static readonly List<BetaToolUnion> ToolDefinitions = AssistantTools.Definitions.Select(d => (BetaToolUnion)new BetaTool
        {
            Name = d.Name,
            Description = d.Description,
            InputSchema = new()
            {
                Properties = d.Properties.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
                Required = d.Required
            }
        }).ToList();

        /// <summary>Claude'un cevabını sohbete geri eklenecek biçime çevirir; araç çağrılarını ve metni ayırır.</summary>
        private static (List<BetaContentBlockParam> Echo, List<BetaToolUseBlock> ToolCalls, string Text) Read(BetaMessage reply)
        {
            List<BetaContentBlockParam> echo = [];
            List<BetaToolUseBlock> calls = [];
            List<string> text = [];
            foreach (BetaContentBlock block in reply.Content)
            {
                if (block.TryPickText(out BetaTextBlock? t))
                {
                    echo.Add(new BetaTextBlockParam { Text = t.Text });
                    text.Add(t.Text);
                }
                else if (block.TryPickThinking(out BetaThinkingBlock? th))
                    echo.Add(new BetaThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                else if (block.TryPickRedactedThinking(out BetaRedactedThinkingBlock? rt))
                    echo.Add(new BetaRedactedThinkingBlockParam { Data = rt.Data });
                else if (block.TryPickToolUse(out BetaToolUseBlock? tu))
                {
                    echo.Add(new BetaToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                    calls.Add(tu);
                }
            }
            return (echo, calls, string.Join("\n\n", text).Trim());
        }

        /// <summary>
        /// İstemcinin gönderdiği sohbeti (sadece metin) mesajlara çevirir: son 12 mesaj, her biri en fazla 4.000 karakter,
        /// ilk mesaj kullanıcıdan. Son soruya şu anki Türkiye saati eklenir ("bugün", "son 1 saat" doğru hesaplansın diye).
        /// </summary>
        private static List<BetaMessageParam> BuildHistory(AssistantRequest request)
        {
            // Cevapsız kalmış bir sorudan sonra art arda iki kullanıcı mesajı gelebilir: aynı roldekiler birleştirilir
            List<AssistantMessage> history = [];
            foreach (AssistantMessage m in request.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Content) && m.Role is "user" or "assistant"))
            {
                if (history.Count > 0 && history[^1].Role == m.Role)
                    history[^1] = new AssistantMessage { Role = m.Role, Content = $"{history[^1].Content}\n\n{m.Content}" };
                else
                    history.Add(m);
            }
            history = history.TakeLast(MaxHistoryMessages).SkipWhile(m => m.Role != "user").ToList();
            if (history.Count == 0 || history[^1].Role != "user") return [];

            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
            DateTimeOffset now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            List<BetaMessageParam> messages = [];
            for (int i = 0; i < history.Count; i++)
            {
                string content = history[i].Content.Length > MaxMessageChars ? history[i].Content[..MaxMessageChars] : history[i].Content;
                if (i == history.Count - 1)
                    content = $"[Şu an: {now:yyyy-MM-dd HH:mm} Türkiye saati ({now:zzz})]\n\n{content}";
                messages.Add(new BetaMessageParam { Role = history[i].Role == "user" ? Role.User : Role.Assistant, Content = content });
            }
            return messages;
        }
    }
}

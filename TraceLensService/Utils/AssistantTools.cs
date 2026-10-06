using System.Text.Json;
using System.Text.Json.Serialization;
using CommonUtils.Models;
using TraceLensService.Business;
using TraceLensService.Enums;
using TraceLensService.Models.Requests;

namespace TraceLensService.Utils
{
    /// <summary>
    /// AI asistanının kullanabildiği araçlar. Her biri TraceLens'in mevcut iş katmanını çağırır (dashboard ile aynı sayılar);
    /// Claude veritabanına doğrudan erişmez, SQL yazmaz. Sonuçlar gereksiz alanlardan (grafik dizileri vb.) arındırılıp
    /// JSON olarak döner. Hepsi salt okunurdur.
    /// </summary>
    public class AssistantTools(IServiceProvider services)
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Ekranda gösterilen adım adları ("Sorunlar incelendi").</summary>
        public static readonly Dictionary<string, string> Labels = new()
        {
            ["get_overview"] = "Genel bakış incelendi",
            ["get_issues"] = "Sorunlar incelendi",
            ["list_operations"] = "Endpoint ve görevler incelendi",
            ["get_service_breakdown"] = "Servisin içi incelendi",
            ["get_error_types"] = "Hata türleri incelendi",
            ["find_slowest_requests"] = "En yavaş istekler incelendi",
            ["get_alerts"] = "Alarmlar incelendi",
            ["get_report"] = "Rapor incelendi",
            ["get_trace"] = "Trace incelendi"
        };

        /// <summary>Araç tanımları: ad, açıklama, JSON şeması (Claude'a gönderilir).</summary>
        public static IReadOnlyList<(string Name, string Description, Dictionary<string, object> Properties, string[] Required)> Definitions { get; } =
        [
            ("get_overview",
             "Tüm servis ve görevlerin özeti: toplam istek, ortalama ve p95 süre, eşiği aşan ve hatalı sayısı (önceki eşit dönemle karşılaştırmalı), " +
             "uygulama kartları (durum: ok/slow/error), en yavaş ve en çok hata veren endpoint/görevler, son hatalar, açık alarm sayısı. " +
             "Genel sorularda ilk bakılacak yer.",
             TimeProps(), []),
            ("get_issues",
             "Dikkat isteyen endpoint/görevler: ortalaması eşiğini aşanlar ve hata oranı %5'i geçenler, en sık hata ve alarm bilgisiyle, en kötüden başlayarak.",
             TimeProps(("service", Str("Sadece bu uygulama (ör. order-service)"))), []),
            ("list_operations",
             "Bir uygulama türündeki tüm endpoint (servis) ya da görev (scheduler) satırları: istek sayısı, ortalama, p95, max, eşiği aşan, hata sayısı, eşik.",
             TimeProps(("app", AppProp()), ("service", Str("Sadece bu uygulama"))), ["app"]),
            ("get_service_breakdown",
             "Tek bir uygulamanın içi: sürenin kendi kodu / başka servislere çağrılar / veritabanı arasında dağılımı, en çok süre alan metodlar, DB sorguları " +
             "(N+1 şüphesiyle) ve dış çağrılar. 'X neden yavaş' sorularında kullan.",
             TimeProps(("app", AppProp()), ("service", Str("Uygulama adı, ör. order-service"))), ["app", "service"]),
            ("get_error_types",
             "Durum kodu dağılımı (200/500/502…, görevlerde başarılı/başarısız) ve hata türleri (exception tipi ya da HTTP kodu), her birinin en çok görüldüğü operasyon.",
             TimeProps(("app", AppProp()), ("service", Str("Sadece bu uygulama")), ("operation", Str("Sadece bu endpoint/görev, tam adıyla"))), ["app"]),
            ("find_slowest_requests",
             "En yavaş tekil istekler (en fazla 10): zaman, uygulama, operasyon, süre, sonuç, traceId. Örnek istek göstermek için.",
             TimeProps(("app", AppProp()), ("service", Str("Sadece bu uygulama")), ("operation", Str("Sadece bu endpoint/görev")),
                       ("onlyErrors", new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Sadece hatalı istekler" })), ["app"]),
            ("get_alerts",
             "Açık ve kapanan alarmlar. Tür: slow (süre eşiği aşıldı) ya da error (hata oranı %5'i geçti, en sık hata kodu ile).",
             new Dictionary<string, object>
             {
                 ["days"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Son kaç gün (1-90), varsayılan 7" },
                 ["kind"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "slow", "error" } },
                 ["service"] = Str("Sadece bu uygulama"),
                 ["status"] = Str("Hata kodu: 500 gibi tam kod ya da 5xx / 4xx")
             }, []),
            ("get_report",
             "Gün bazlı rapor ve önceki eşit dönemle karşılaştırma: toplamlar, endpoint/görev bazında ortalama ve önceki ortalama, hata oranları, alarmlar. " +
             "Dün, geçen hafta, son 30 gün, 'dünle bugünü karşılaştır' gibi sorular için. Günler Türkiye saatine göre.",
             new Dictionary<string, object>
             {
                 ["period"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "today", "yesterday", "7d", "30d" } }
             }, ["period"]),
            ("get_trace",
             "Tek bir isteğin adım adım dökümü (waterfall): her adımın süresi, kendi süresi, hata durumu ve otomatik ipuçları. traceId gerekir.",
             new Dictionary<string, object> { ["traceId"] = Str("Trace id (find_slowest_requests ya da son hatalardan)") }, ["traceId"])
        ];

        /// <summary>Aracı çalıştırır; sonuç JSON metni. Hata olursa (isError, mesaj) döner, istisna fırlatmaz.</summary>
        public async Task<(string Content, bool IsError)> ExecuteAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
        {
            try
            {
                object? result = name switch
                {
                    "get_overview" => Overview(Unwrap(await Get<OverviewBusiness>().GetOverview(Filter(input)))),
                    "get_issues" => Unwrap(await Get<OverviewBusiness>().GetIssues(Filter(input))).Take(20),
                    "list_operations" => Unwrap(await Get<TraceBusiness>().GetSummary(App(input), Filter(input))).Take(60),
                    "get_service_breakdown" => Breakdown(Unwrap(await Get<TraceBusiness>().GetBreakdown(Text(input, "service") ?? string.Empty, Filter(input)))),
                    "get_error_types" => Unwrap(await Get<TraceBusiness>().GetOutcomes(App(input), Filter(input))),
                    "find_slowest_requests" => Unwrap(await Get<TraceBusiness>().GetRequests(App(input), Filter(input), "duration", 10, 0)).Items
                        .Select(r => new { r.Timestamp, r.Service, r.Operation, r.DurationMs, r.Status, r.HttpStatusCode, r.StatusMessage, r.TraceId }),
                    "get_alerts" => Alerts(Unwrap(await Get<AlertBusiness>().GetAlerts(new AlertFilterRequest
                    {
                        Days = Int(input, "days"),
                        Kind = Text(input, "kind"),
                        Service = Text(input, "service"),
                        Status = Text(input, "status")
                    }))),
                    "get_report" => Report(Unwrap(await Get<ReportBusiness>().GetReport(Text(input, "period"), null, null))),
                    "get_trace" => Trace(Unwrap(await Get<TraceBusiness>().GetTrace(Text(input, "traceId") ?? string.Empty))),
                    _ => throw new ArgumentException($"Bilinmeyen araç: {name}")
                };
                return (JsonSerializer.Serialize(result, Json), false);
            }
            catch (Exception ex)
            {
                return (ex.Message, true);
            }
        }

        #region Sonuçları kısaltma (grafik dizileri ve tekrar eden alanlar Claude'a gönderilmez)

        private static object Overview(Models.Responses.Overview.OverviewResponse o) => new
        {
            o.From,
            o.To,
            o.Totals,
            o.PreviousTotals,
            Apps = o.Services.Select(s => new
            {
                s.Service, s.App, s.Status, s.Count, s.AvgMs, s.P95Ms, s.ErrorCount, s.ErrorRate,
                s.SlowOperationCount, s.SlowestOperation, s.SlowestOperationAvgMs, s.SlowestOperationThresholdMs
            }),
            o.SlowestOperations,
            o.MostErrors,
            RecentErrors = o.RecentErrors.Take(5)
        };

        private static object Breakdown(Models.Responses.ServiceDetail.ServiceBreakdownResponse b) => new
        {
            b.Service,
            b.RequestCount,
            b.RequestTotalMs,
            b.TimeSplit,
            Methods = b.Methods.Take(10),
            Database = b.Database.Take(10),
            Calls = b.Calls.Take(10)
        };

        private static object Alerts(Models.Responses.Alerts.AlertListResponse a) => new
        {
            a.From,
            a.To,
            Active = a.Active.Select(Alert),
            Resolved = a.History.Take(30).Select(Alert)
        };

        private static object Alert(Models.DbModels.AlertRecord a) => new
        {
            a.Kind, a.App, a.Service, a.Operation, a.Metric, a.ValueMs, a.PeakValueMs, a.ThresholdMs,
            a.ErrorRate, a.PeakErrorRate, a.TopStatus, a.FiredAt, a.ResolvedAt, a.DurationMinutes
        };

        private static object Report(Models.Responses.Reports.ReportResponse r) => new
        {
            r.Period, r.From, r.To, r.PreviousFrom, r.PreviousTo, r.IncludesToday, r.DataSince,
            r.Totals, r.PreviousTotals,
            Daily = r.Daily.Select(d => new { d.Time, d.Count, d.AvgMs, d.P95Ms, d.ErrorCount, d.SlowCount }),
            Operations = r.Operations.Take(30),
            r.Alerts
        };

        private static object Trace(Models.Responses.Traces.TraceDetailResponse t) => new
        {
            t.TraceId, t.StartTime, t.DurationMs, t.SpanCount, t.Services, t.Hints,
            Spans = t.Spans.Take(40).Select(s => new { s.Depth, s.Service, s.Name, s.Kind, s.DurationMs, s.SelfMs, s.Status, s.StatusMessage })
        };

        #endregion

        #region Girdi okuma

        private T Get<T>() where T : notnull => services.GetRequiredService<T>();

        private static T Unwrap<T>(DataResponse<T> response) where T : class =>
            response.IsSuccess && response.Data is not null ? response.Data : throw new InvalidOperationException(response.Message);

        private static TraceFilterRequest Filter(IReadOnlyDictionary<string, JsonElement> input) => new()
        {
            Range = Text(input, "range"),
            From = Time(input, "from"),
            To = Time(input, "to"),
            Service = Text(input, "service"),
            Operation = Text(input, "operation"),
            OnlyErrors = input.TryGetValue("onlyErrors", out JsonElement e) && e.ValueKind == JsonValueKind.True
        };

        private static AppKind App(IReadOnlyDictionary<string, JsonElement> input) =>
            Text(input, "app") == "scheduler" ? AppKind.Scheduler : AppKind.Service;

        private static string? Text(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            input.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

        private static int? Int(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            input.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;

        private static DateTimeOffset? Time(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            Text(input, key) is string s && DateTimeOffset.TryParse(s, out DateTimeOffset t) ? t : null;

        #endregion

        #region Şema yardımcıları

        private static Dictionary<string, object> Str(string description) => new() { ["type"] = "string", ["description"] = description };

        private static Dictionary<string, object> AppProp() => new()
        {
            ["type"] = "string",
            ["enum"] = new[] { "service", "scheduler" },
            ["description"] = "service: HTTP servisleri; scheduler: zamanlanmış görevler"
        };

        // Zaman aralığı: hazır aralık ya da from/to (ISO 8601, saat dilimiyle). İkisi de yoksa son 1 saat.
        private static Dictionary<string, object> TimeProps(params (string Name, Dictionary<string, object> Schema)[] extra)
        {
            Dictionary<string, object> props = new()
            {
                ["range"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "15m", "1h", "6h", "24h", "7d" },
                    ["description"] = "Şimdiden geriye aralık; from/to verilmezse kullanılır (varsayılan 1h). Ham veri en fazla 7 gün geriye gider."
                },
                ["from"] = Str("Başlangıç, ISO 8601 (ör. 2026-10-06T00:00:00+03:00). 'Bugün' için Türkiye saatiyle gece yarısı."),
                ["to"] = Str("Bitiş, ISO 8601; verilmezse şimdi")
            };
            foreach ((string name, Dictionary<string, object> schema) in extra) props[name] = schema;
            return props;
        }

        #endregion
    }
}

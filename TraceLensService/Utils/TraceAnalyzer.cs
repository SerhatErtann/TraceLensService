using TraceLensService.Common;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Responses.Traces;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Ham span'lerden waterfall ağacını kurar, self-time hesaplar ve kök neden ipuçları çıkarır.
    /// </summary>
    public static class TraceAnalyzer
    {
        private const int NPlusOneThreshold = 10;
        private const long LargePayloadBytes = 1_000_000;
        private const double DominantSpanRatio = 0.5;

        public static TraceDetailResponse Build(string traceId, List<RawSpan> spans)
        {
            DateTime start = spans.Min(s => s.Timestamp);
            DateTime end = spans.Max(s => s.Timestamp.AddMilliseconds(s.DurationMs));
            HashSet<string> ids = spans.Select(s => s.SpanId).ToHashSet();
            ILookup<string, RawSpan> children = spans
                .Where(s => s.ParentSpanId is not null && ids.Contains(s.ParentSpanId))
                .ToLookup(s => s.ParentSpanId!);

            // Parent'ı bu trace'te olmayan span'ler kök kabul edilir (ör. eksik gelen parent).
            List<RawSpan> roots = spans.Where(s => s.ParentSpanId is null || !ids.Contains(s.ParentSpanId)).ToList();

            List<SpanNodeResponse> ordered = new(spans.Count);
            foreach (RawSpan root in roots)
                Visit(root, 0);

            double totalMs = Math.Round((end - start).TotalMilliseconds, 2);
            return new TraceDetailResponse
            {
                TraceId = traceId,
                StartTime = start,
                DurationMs = totalMs,
                SpanCount = spans.Count,
                Services = spans.Select(s => s.Service).Distinct().ToList(),
                Spans = ordered,
                Hints = BuildHints(ordered, children, totalMs)
            };

            void Visit(RawSpan span, int depth)
            {
                List<RawSpan> kids = children[span.SpanId].OrderBy(c => c.Timestamp).ToList();
                ordered.Add(new SpanNodeResponse
                {
                    SpanId = span.SpanId,
                    ParentSpanId = span.ParentSpanId,
                    Service = span.Service,
                    Name = span.Name,
                    Kind = span.Kind,
                    Timestamp = span.Timestamp,
                    StartOffsetMs = Math.Round((span.Timestamp - start).TotalMilliseconds, 2),
                    DurationMs = Math.Round(span.DurationMs, 2),
                    SelfMs = Math.Round(SelfTime(span, kids), 2),
                    Depth = depth,
                    Status = span.Status,
                    StatusMessage = span.StatusMessage,
                    Attributes = span.Attributes,
                    Events = span.Events
                });

                foreach (RawSpan kid in kids)
                    Visit(kid, depth + 1);
            }
        }

        /// <summary>Span süresinden, çocukların kapladığı (birleştirilmiş) zaman aralığını çıkarır.</summary>
        private static double SelfTime(RawSpan span, List<RawSpan> kids)
        {
            if (kids.Count == 0) return span.DurationMs;

            DateTime spanEnd = span.Timestamp.AddMilliseconds(span.DurationMs);
            double covered = 0;
            DateTime? curStart = null, curEnd = null;
            foreach (RawSpan k in kids)
            {
                DateTime s = k.Timestamp < span.Timestamp ? span.Timestamp : k.Timestamp;
                DateTime e = k.Timestamp.AddMilliseconds(k.DurationMs);
                if (e > spanEnd) e = spanEnd;
                if (e <= s) continue;

                if (curEnd is null || s > curEnd)
                {
                    if (curEnd is not null) covered += (curEnd.Value - curStart!.Value).TotalMilliseconds;
                    curStart = s;
                    curEnd = e;
                }
                else if (e > curEnd)
                {
                    curEnd = e;
                }
            }
            if (curEnd is not null) covered += (curEnd.Value - curStart!.Value).TotalMilliseconds;

            return Math.Max(0, span.DurationMs - covered);
        }

        private static List<TraceHintResponse> BuildHints(List<SpanNodeResponse> spans, ILookup<string, RawSpan> children, double totalMs)
        {
            List<TraceHintResponse> hints = [];

            SpanNodeResponse? dominant = spans.MaxBy(s => s.SelfMs);
            if (dominant is not null && totalMs > 0 && dominant.SelfMs / totalMs >= DominantSpanRatio)
                hints.Add(Hint("slow-span", "warning",
                    $"Sürenin %{dominant.SelfMs / totalMs * 100:0}'i '{dominant.Name}' içinde geçiyor ({dominant.SelfMs:0} ms).",
                    dominant.SpanId));

            // Aynı operasyon trace içinde birden çok kez N+1 yapıyorsa tek satırda toplanır.
            var nPlusOne = spans
                .Select(parent => (parent, db: children[parent.SpanId].Where(IsDbSpan).ToList()))
                .Where(x => x.db.Count >= NPlusOneThreshold)
                .GroupBy(x => (x.parent.Service, x.parent.Name));
            foreach (var group in nPlusOne)
            {
                string times = group.Count() > 1 ? $" ({group.Count()} kez)" : string.Empty;
                hints.Add(Hint("n-plus-one", "warning",
                    $"'{group.Key.Name}' içinde {group.Max(x => x.db.Count)} DB sorgusu var{times}, " +
                    $"toplam {group.Sum(x => x.db.Sum(d => d.DurationMs)):0} ms. Döngü içinde sorgu (N+1) olabilir.",
                    group.First().parent.SpanId));
            }

            foreach (SpanNodeResponse span in spans)
            {
                if (span.Attributes.TryGetValue("http.response.body.size", out string? raw)
                    && long.TryParse(raw, out long bytes) && bytes >= LargePayloadBytes)
                    hints.Add(Hint("large-payload", "info", $"'{span.Name}' yanıtı {bytes / 1024.0 / 1024.0:0.0} MB.", span.SpanId));
            }

            // Hata zincir boyunca yukarı taşınır; sadece kaynağı (altında başka hatalı span olmayanı) göster.
            List<SpanNodeResponse> errorSpans = spans.Where(s => s.Status == GlobalConsts.ErrorStatus).ToList();
            HashSet<string> hasErrorChild = errorSpans.Select(s => s.ParentSpanId).OfType<string>().ToHashSet();
            foreach (SpanNodeResponse span in errorSpans.Where(s => !hasErrorChild.Contains(s.SpanId)))
            {
                int propagated = CountErrorAncestors(span, spans);
                string suffix = propagated > 0 ? $" Hata {propagated} üst adıma yayıldı." : string.Empty;
                hints.Add(Hint("error", "error",
                    $"Hatanın kaynağı '{span.Name}' ({span.Service}): {DescribeError(span)}.{suffix}", span.SpanId));
            }

            return hints;
        }

        private static TraceHintResponse Hint(string type, string severity, string message, string? spanId) =>
            new() { Type = type, Severity = severity, Message = message, SpanId = spanId };

        private static string DescribeError(SpanNodeResponse span)
        {
            SpanEventResponse? exception = span.Events.FirstOrDefault(e => e.Name == "exception");
            if (exception is not null && exception.Attributes.TryGetValue("exception.message", out string? message))
                return exception.Attributes.TryGetValue("exception.type", out string? type) ? $"{type}: {message}" : message;
            if (!string.IsNullOrEmpty(span.StatusMessage))
                return span.StatusMessage;
            if (span.Attributes.TryGetValue("http.response.status_code", out string? code))
                return $"HTTP {code}";
            return "detay yok";
        }

        private static int CountErrorAncestors(SpanNodeResponse span, List<SpanNodeResponse> spans)
        {
            Dictionary<string, SpanNodeResponse> byId = spans.ToDictionary(s => s.SpanId);
            int count = 0;
            string? current = span.ParentSpanId;
            while (current is not null && byId.TryGetValue(current, out SpanNodeResponse? parent))
            {
                if (parent.Status == GlobalConsts.ErrorStatus) count++;
                current = parent.ParentSpanId;
            }
            return count;
        }

        private static bool IsDbSpan(RawSpan s) =>
            s.Attributes.ContainsKey("db.system") || s.Attributes.ContainsKey("db.system.name");
    }
}

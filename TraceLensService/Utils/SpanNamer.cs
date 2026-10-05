using System.Text.RegularExpressions;
using TraceLensService.Models.Internal;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Waterfall'da okunur ad üretir. Resmi OpenTelemetry paketleri DB span'ine veritabanı adını ("main"),
    /// giden HTTP çağrısına sadece metodu ("GET") verir; burada SQL'den ve URL'den anlamlı ad çıkarılır.
    /// </summary>
    public static partial class SpanNamer
    {
        public static string DisplayName(RawSpan span)
        {
            string? sql = Attr(span, "db.query.text") ?? Attr(span, "db.statement");
            if (sql is not null && SqlName(sql) is string sqlName)
                return sqlName;

            string? method = Attr(span, "http.request.method") ?? Attr(span, "http.method");
            string? url = Attr(span, "url.full") ?? Attr(span, "http.url");
            if (span.Kind == "Client" && method is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return $"{method} {uri.Authority}{uri.AbsolutePath}";

            return span.Name;
        }

        private static string? SqlName(string sql)
        {
            Match verb = VerbRegex().Match(sql);
            if (!verb.Success) return null;

            Match table = TableRegex().Match(sql);
            return table.Success
                ? $"{verb.Value.ToUpperInvariant()} {table.Groups["table"].Value}"
                : verb.Value.ToUpperInvariant();
        }

        private static string? Attr(RawSpan span, string key) =>
            span.Attributes.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

        [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC)\b", RegexOptions.IgnoreCase)]
        private static partial Regex VerbRegex();

        [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE|JOIN)\s+[\[""`]?(?:\w+[\]""`]?\.)?[\[""`]?(?<table>\w+)", RegexOptions.IgnoreCase)]
        private static partial Regex TableRegex();
    }
}

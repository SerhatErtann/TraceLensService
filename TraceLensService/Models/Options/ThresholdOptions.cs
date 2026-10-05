namespace TraceLensService.Models.Options
{
    public class ThresholdOptions
    {
        public const string SectionName = "Thresholds";

        /// <summary>Varsayılan "yavaş istek" eşiği (ms).</summary>
        public double DefaultMs { get; set; } = 200;

        /// <summary>
        /// Operasyon bazlı eşikler. Anahtar "servis|operasyon",
        /// örn. "order-service|GET /orders/report" veya "order-scheduler|JOB ReportExport".
        /// </summary>
        public Dictionary<string, double> Overrides { get; set; } = [];

        public double For(string service, string operation) =>
            Overrides.TryGetValue($"{service}|{operation}", out double ms) ? ms : DefaultMs;

        /// <summary>
        /// Satır bazında eşiği nanosaniye cinsinden döndüren ClickHouse ifadesi üretir;
        /// böylece "yavaş istek sayısı" override'larla tutarlı hesaplanır.
        /// </summary>
        public string ToSqlNanos(Dictionary<string, object> parameters)
        {
            parameters["thrDefault"] = ToNanos(DefaultMs);
            if (Overrides.Count == 0)
                return "{thrDefault:UInt64}";

            List<string> branches = [];
            int i = 0;
            foreach ((string key, double ms) in Overrides)
            {
                parameters[$"thrKey{i}"] = key;
                parameters[$"thrVal{i}"] = ToNanos(ms);
                branches.Add($"concat(ServiceName, '|', SpanName) = {{thrKey{i}:String}}, {{thrVal{i}:UInt64}}");
                i++;
            }
            return $"multiIf({string.Join(", ", branches)}, {{thrDefault:UInt64}})";
        }

        public static ulong ToNanos(double ms) => (ulong)(ms * 1_000_000);
    }
}

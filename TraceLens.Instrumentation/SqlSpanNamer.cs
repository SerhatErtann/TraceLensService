using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TraceLens.Instrumentation;

/// <summary>
/// DB span'lerine veritabanı adı yerine "SELECT Orders" gibi okunabilir bir ad verir.
/// </summary>
internal static partial class SqlSpanNamer
{
    public static void Apply(Activity activity, string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        var verb = VerbRegex().Match(sql);
        if (!verb.Success) return;

        var table = TableRegex().Match(sql);
        activity.DisplayName = table.Success
            ? $"{verb.Value.ToUpperInvariant()} {table.Groups["table"].Value}"
            : verb.Value.ToUpperInvariant();
    }

    [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VerbRegex();

    [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE|JOIN)\s+[\[""`]?(?:\w+[\]""`]?\.)?[\[""`]?(?<table>\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex TableRegex();
}

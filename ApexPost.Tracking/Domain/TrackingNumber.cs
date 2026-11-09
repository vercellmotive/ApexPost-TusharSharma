using System.Text.RegularExpressions;

namespace ApexPost.Tracking.Domain;

/// <summary>The tracking number format, <c>APX-</c> followed by eight digits.</summary>
public static partial class TrackingNumber
{
    public const string Pattern = @"^APX-\d{8}$";

    // ECMAScript mode makes \d match only 0-9, not every Unicode digit.
    [GeneratedRegex(Pattern, RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Format();

    public static bool IsValid(string? value) => value is not null && Format().IsMatch(value);
}

/// <summary>The scan id format shared with the Part A courier store: <c>scan::</c> followed by a lower-case GUID.</summary>
public static class ScanId
{
    public const string Prefix = "scan::";
    public const string Pattern = "^scan::[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

    public static bool TryParse(string? value, out Guid id)
    {
        id = Guid.Empty;
        return value is not null
            && value.StartsWith(Prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(value.AsSpan(Prefix.Length), "D", out id)
            && Format(id) == value;
    }

    public static string Format(Guid id) => $"{Prefix}{id:D}";
}

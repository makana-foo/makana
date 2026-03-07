using Humanizer;

namespace Makana.WebConsole.Models;

public static class WebConsoleDisplayFormatter
{
    public static DateTimeOffset? ToDateTimeOffset(long? unixTimestamp)
    {
        if (!unixTimestamp.HasValue || unixTimestamp.Value <= 0)
        {
            return null;
        }

        return unixTimestamp.Value > 1_000_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp.Value)
            : DateTimeOffset.FromUnixTimeSeconds(unixTimestamp.Value);
    }

    public static string FormatRelativeTime(long? unixTimestamp, string emptyText = "n/a")
        => FormatRelativeTime(ToDateTimeOffset(unixTimestamp), emptyText);

    public static string FormatRelativeTime(DateTimeOffset? timestamp, string emptyText = "n/a")
    {
        if (timestamp is null)
        {
            return emptyText;
        }

        return timestamp.Value.UtcDateTime.Humanize(dateToCompareAgainst: DateTime.UtcNow);
    }

    public static string FormatExactTime(long? unixTimestamp, string emptyText = "n/a")
        => FormatExactTime(ToDateTimeOffset(unixTimestamp), emptyText);

    public static string FormatExactTime(DateTimeOffset? timestamp, string emptyText = "n/a")
    {
        if (timestamp is null)
        {
            return emptyText;
        }

        return timestamp.Value.ToLocalTime().ToString("ddd, MMM d, yyyy, h:mm:ss tt zzz");
    }

    public static string FormatUpdateKindLabel(string? kind, bool isPreview)
    {
        var baseLabel = FormatKindLabel(kind);
        if (!isPreview)
        {
            return baseLabel;
        }

        return string.Equals(baseLabel, "Preview", StringComparison.Ordinal)
            ? "Preview"
            : $"{baseLabel} (Preview)";
    }

    public static string FormatUpdateRunLabel(string? kind, bool isPreview, int? version)
    {
        var kindLabel = FormatUpdateKindLabel(kind, isPreview);
        return !isPreview && version is > 0
            ? $"{kindLabel} #{version}"
            : kindLabel;
    }

    public static string FormatUpdateHistoryTitle(int? version)
        => version is > 0 ? $"Update #{version}" : "Update";

    public static string FormatKindLabel(string? kind)
        => (kind ?? string.Empty).ToLowerInvariant() switch
        {
            "preview" => "Preview",
            "update" => "Update",
            "import" => "Import",
            "refresh" => "Refresh",
            "destroy" => "Destroy",
            "rename" => "Rename",
            "" => "Update",
            var other => char.ToUpperInvariant(other[0]) + other[1..]
        };
}

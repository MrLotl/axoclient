namespace AxoClient.Core;

public static class Formats
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        _ => $"{bytes / 1024.0:0} KB"
    };

    public static string Megabytes(int mb) => mb % 1024 == 0 ? $"{mb / 1024} GB" : $"{mb / 1024.0:0.#} GB";

    public static string Duration(long seconds)
    {
        if (seconds < 60)
            return seconds > 0 ? "unter 1 Min" : "–";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} Std {span.Minutes} Min" : $"{span.Minutes} Min";
    }

    public static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 2)
            return "gerade eben";
        if (age.TotalHours < 1)
            return $"vor {(int)age.TotalMinutes} Min";
        if (age.TotalDays < 1)
            return $"vor {(int)age.TotalHours} Std";
        return age.TotalDays < 2 ? "gestern" : $"vor {(int)age.TotalDays} Tagen";
    }

    public static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    public static string Some(IReadOnlyCollection<string> items, int max, string separator = ", ") =>
        string.Join(separator, items.Take(max)) + (items.Count > max ? $" und {items.Count - max} weitere" : "");
}

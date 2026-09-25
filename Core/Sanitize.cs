using System.Text;

namespace AxoClient.Core;

public static class Sanitize
{
    public static string Text(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var c in text)
        {
            if (!char.IsControl(c))
                builder.Append(c);
            if (builder.Length >= maxLength)
                break;
        }
        return builder.ToString().Trim();
    }

    public static string FileName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return safe.Length == 0 ? fallback : safe;
    }

    public static string StripFileName(string name, int maxLength)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}

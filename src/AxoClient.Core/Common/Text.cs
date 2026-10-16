using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AxoClient.Core.Common;

public static class Text
{
    /// <summary>Kürzt fremde Texte, entfernt Steuerzeichen. Für alles, was aus dem Netz oder von Freunden kommt.</summary>
    public static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var c in text)
        {
            if (builder.Length >= maxLength)
                break;
            if (c is '\n' or '\t')
                builder.Append(' ');
            else if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                builder.Append(c);
        }
        return builder.ToString().Trim();
    }

    /// <summary>Mehrzeiliger fremder Text (Beschreibungen, Changelogs): Zeilenumbrüche bleiben.</summary>
    public static string CleanMultiline(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var c in text)
        {
            if (builder.Length >= maxLength)
                break;
            if (c == '\n' || (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format))
                builder.Append(c);
        }
        return builder.ToString().Trim();
    }

    /// <summary>"Survival mit Freunden" → "survival-mit-freunden-7f3a".</summary>
    public static string NewId(string name)
    {
        var slug = Slug(name);
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
        return string.IsNullOrEmpty(slug) ? suffix : $"{slug}-{suffix}";
    }

    public static string Slug(string name)
    {
        var lowered = name.Trim().ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var normalized = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var lastDash = true;
        foreach (var c in normalized)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                lastDash = false;
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
            if (builder.Length >= 40)
                break;
        }
        return builder.ToString().Trim('-');
    }

    /// <summary>Gültige IDs, wie sie von der UI kommen dürfen.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
        && !id.StartsWith('-');

    public static string RequireId(string? id) =>
        IsValidId(id) ? id! : throw new AxoException("errors.invalidId");

    public static string Sha1Hex(Stream stream) => Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    public static string Sha512Hex(Stream stream) => Convert.ToHexString(SHA512.HashData(stream)).ToLowerInvariant();
    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

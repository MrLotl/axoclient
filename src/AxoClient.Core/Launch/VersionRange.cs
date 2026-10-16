using System.Text.RegularExpressions;

namespace AxoClient.Core.Launch;

/// <summary>
/// Prüft Versionsbedingungen aus Mod-Metadaten: Fabric/Quilt-Prädikate (<c>&gt;=1.21</c>, <c>~1.21.4</c>, <c>1.21.x</c>,
/// <c>*</c>, mit <c>||</c> und Leerzeichen für UND) sowie Maven-Bereiche (<c>[1.20.1,1.21)</c>).
/// Unbekannte Schreibweisen gelten als erfüllt, damit es keine falschen Warnungen gibt.
/// </summary>
public static partial class VersionRange
{
    public static bool Satisfies(string version, string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
            return true;
        range = range.Trim();
        try
        {
            if (range[0] is '[' or '(')
                return MavenSatisfies(version, range);
            return range.Split("||").Any(alternative => alternative.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(predicate => PredicateSatisfies(version, predicate.Trim())));
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static bool PredicateSatisfies(string version, string predicate)
    {
        if (predicate is "*" or "" or "x" or "X")
            return true;
        var match = Predicate().Match(predicate);
        if (!match.Success)
            return true;
        var op = match.Groups[1].Value;
        var target = match.Groups[2].Value;

        if (target.Contains('x') || target.Contains('X') || target.EndsWith(".*"))
        {
            var prefix = target.Split('.').TakeWhile(p => p is not ("x" or "X" or "*")).ToArray();
            var parts = Parts(version);
            return prefix.Select((p, i) => i < parts.Length && parts[i].ToString() == p).All(b => b);
        }

        var cmp = Compare(version, target);
        return op switch
        {
            ">=" => cmp >= 0,
            ">" => cmp > 0,
            "<=" => cmp <= 0,
            "<" => cmp < 0,
            "=" or "" => cmp == 0 || (op == "" && IsPrefix(target, version)),
            "~" => cmp >= 0 && SameMinor(version, target),
            "^" => cmp >= 0 && Parts(version)[0] == Parts(target)[0],
            _ => true
        };
    }

    private static bool IsPrefix(string target, string version) =>
        version.StartsWith(target + ".", StringComparison.Ordinal) && Parts(target).Length < 3;

    private static bool SameMinor(string a, string b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        return pa[0] == pb[0] && pa[1] == pb[1];
    }

    private static bool MavenSatisfies(string version, string range)
    {
        // Mehrere Bereiche: [1.0,1.1),[1.2,)
        foreach (var part in MavenParts().Matches(range).Select(m => m.Value))
        {
            var inclusiveLow = part[0] == '[';
            var inclusiveHigh = part[^1] == ']';
            var inner = part[1..^1].Split(',');
            if (inner.Length == 1)
            {
                if (Compare(version, inner[0].Trim()) == 0)
                    return true;
                continue;
            }
            var low = inner[0].Trim();
            var high = inner[1].Trim();
            var okLow = low.Length == 0 || (inclusiveLow ? Compare(version, low) >= 0 : Compare(version, low) > 0);
            var okHigh = high.Length == 0 || (inclusiveHigh ? Compare(version, high) <= 0 : Compare(version, high) < 0);
            if (okLow && okHigh)
                return true;
        }
        return false;
    }

    public static int Compare(string a, string b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        for (var i = 0; i < 3; i++)
            if (pa[i] != pb[i])
                return pa[i].CompareTo(pb[i]);
        // Vorabversionen (-beta, -rc) sind kleiner als die fertige Version
        var preA = a.Contains('-');
        var preB = b.Contains('-');
        return preA == preB ? 0 : preA ? -1 : 1;
    }

    private static int[] Parts(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToList();
        if (parts.Count == 0)
            throw new FormatException();
        while (parts.Count < 3)
            parts.Add(0);
        return parts.ToArray();
    }

    [GeneratedRegex(@"^(>=|<=|>|<|=|~|\^)?\s*([0-9][0-9A-Za-z.\-+*]*)$")]
    private static partial Regex Predicate();

    [GeneratedRegex(@"[\[\(][^\]\)]*[\]\)]")]
    private static partial Regex MavenParts();
}

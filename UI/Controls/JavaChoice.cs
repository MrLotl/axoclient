namespace AxoClient.UI.Controls;

public sealed record JavaChoice(JavaRuntime? Runtime, string Text)
{
    public override string ToString() => Text;

    public static async Task<List<JavaRuntime>> FindAsync(bool rescan = false)
    {
        if (rescan)
            JavaRuntimes.ClearCache();
        return await Task.Run(JavaRuntimes.FindAllCached);
    }

    public static List<JavaChoice> Build(string automaticText, IReadOnlyList<JavaRuntime> found, string? chosen)
    {
        var choices = new List<JavaChoice> { new(null, automaticText) };
        choices.AddRange(found.Select(r => new JavaChoice(r, r.Display)));
        if (chosen is { Length: > 0 } && !found.Any(f => string.Equals(f.Path, chosen, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new JavaChoice(new JavaRuntime(chosen, 0, "Selbst gewählt"),
                File.Exists(chosen) ? $"Selbst gewählt  ·  {chosen}" : $"Fehlt!  ·  {chosen}"));
        return choices;
    }

    public static JavaChoice Find(List<JavaChoice> choices, string? chosen) =>
        choices.FirstOrDefault(c => string.Equals(c.Runtime?.Path, chosen, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
}

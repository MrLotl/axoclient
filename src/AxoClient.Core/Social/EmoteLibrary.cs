using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AxoClient.Core.Common;

namespace AxoClient.Core.Social;

/// <summary>Ein importiertes Emote: Animation als JSON-Datei in <c>emotes/</c>.</summary>
public sealed class EmoteItem
{
    /// <summary><c>c_</c> und 12 Hex-Zeichen aus dem Hash der Animation – bei allen Spielern gleich.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";

    /// <summary>Name der Animation in der Datei (Bedrock/GeckoLib); <c>null</c> = die erste (PlayerAnimator/Emotecraft).</summary>
    public string? Animation { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmoteLibraryDocument : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public List<EmoteItem> Emotes { get; set; } = [];

    /// <summary>Belegung des Emote-Rads (8 Plätze, im Uhrzeigersinn ab oben): Emote-ID oder <c>null</c>.</summary>
    public List<string?>? Slots { get; set; }
}

public sealed record EmoteView(string Id, string Name, string Source, DateTimeOffset CreatedAt);

/// <summary>
/// Eigene Emotes (importierte Animationen) und die Belegung des Emote-Rads. Die eingebauten Emotes liefert die AxoMod
/// selbst; der Launcher kennt nur ihre IDs und Namen für die Anzeige.
/// </summary>
public sealed class EmoteLibrary(AppPaths paths, EventHub events)
{
    public const int SlotCount = 8;

    /// <summary>Höchstgröße der Animation (kompaktes JSON) – so viel darf der Live-Raum weiterreichen.</summary>
    public const int MaxAnimationChars = 200_000;

    private const int MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxEmotes = 60;

    /// <summary>Eingebaute Emotes der AxoMod (ID, Name) in der Reihenfolge der Mod.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> BuiltIn =
    [
        ("wave", "Winken"), ("bow", "Verbeugen"), ("clap", "Klatschen"), ("cheer", "Jubeln"), ("dance", "Tanzen"),
        ("sit", "Hinsetzen"), ("point", "Zeigen"), ("salute", "Salutieren"), ("facepalm", "Facepalm"), ("nod", "Nicken")
    ];

    public static readonly IReadOnlyList<string?> DefaultSlots = ["wave", "cheer", "dance", "clap", "sit", "point", "bow", null];

    private readonly JsonStore<EmoteLibraryDocument> _store = new(paths.Fs, paths.EmoteLibrary);

    public async Task<IReadOnlyList<EmoteView>> ListAsync(CancellationToken ct = default)
    {
        var doc = await _store.LoadAsync(ct);
        return doc.Emotes.Where(e => System.IO.File.Exists(FilePath(e)))
            .Select(e => new EmoteView(e.Id, e.Name, "imported", e.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<string?>> SlotsAsync(CancellationToken ct = default)
    {
        var doc = await _store.LoadAsync(ct);
        return Normalize(doc.Slots, doc.Emotes);
    }

    public async Task SetSlotsAsync(IReadOnlyList<string?> slots, CancellationToken ct = default)
    {
        await _store.UpdateAsync(doc => doc.Slots = Normalize(slots, doc.Emotes).ToList(), ct);
        events.Publish("emotes.changed");
    }

    /// <summary>Importiert eine Animationsdatei (Blockbench/GeckoLib-Bedrock oder PlayerAnimator/Emotecraft). Gibt die neuen Emotes zurück.</summary>
    public async Task<IReadOnlyList<EmoteView>> ImportAsync(string sourcePath, CancellationToken ct = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length > MaxFileBytes)
            throw new AxoException("emotes.tooLarge");
        JsonObject root;
        try
        {
            root = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(sourcePath, ct)) as JsonObject
                   ?? throw new AxoException("emotes.invalidFile");
        }
        catch (JsonException)
        {
            throw new AxoException("emotes.invalidFile");
        }

        var baseName = Text.Clean(Path.GetFileNameWithoutExtension(sourcePath), 24);
        var found = new List<(string Name, string? Key, JsonObject Animation)>();
        if (root["animations"] is JsonObject animations && animations.Count > 0)
        {
            // Bedrock/GeckoLib: jede Animation ein eigenes Emote, mit nur dieser Animation in der Datei
            foreach (var (key, value) in animations)
            {
                if (value is not JsonObject)
                    continue;
                var single = new JsonObject
                {
                    ["format_version"] = root["format_version"]?.DeepClone() ?? "1.8.0",
                    ["animations"] = new JsonObject { [key] = value.DeepClone() }
                };
                found.Add((NiceName(key, animations.Count == 1 ? baseName : null), key, single));
            }
        }
        else if (root["emote"] is JsonObject)
        {
            // PlayerAnimator/Emotecraft: eine Animation je Datei
            var name = root["name"]?.GetValueKind() == JsonValueKind.String ? Text.Clean(root["name"]!.GetValue<string>(), 24) : baseName;
            found.Add((name is { Length: > 0 } ? name : baseName, null, (JsonObject)root.DeepClone()));
        }
        if (found.Count == 0)
            throw new AxoException("emotes.invalidFile");

        Directory.CreateDirectory(paths.Emotes);
        var added = new List<EmoteItem>();
        foreach (var (name, key, animation) in found)
        {
            var compact = animation.ToJsonString(Json.Compact);
            if (compact.Length > MaxAnimationChars)
                throw new AxoException("emotes.tooLarge");
            var id = "c_" + Text.Sha256Hex(Encoding.UTF8.GetBytes(compact))[..12];
            await System.IO.File.WriteAllTextAsync(Path.Combine(paths.Emotes, id + ".json"), compact, ct);
            added.Add(new EmoteItem { Id = id, Name = name is { Length: > 0 } ? name : "Emote", File = id + ".json", Animation = key });
        }

        await _store.UpdateAsync(doc =>
        {
            foreach (var item in added)
            {
                doc.Emotes.RemoveAll(e => e.Id == item.Id);
                doc.Emotes.Insert(0, item);
            }
            if (doc.Emotes.Count > MaxEmotes)
                throw new AxoException("emotes.tooMany");
        }, ct);
        events.Publish("emotes.changed");
        return added.Select(e => new EmoteView(e.Id, e.Name, "imported", e.CreatedAt)).ToList();
    }

    public async Task RenameAsync(string id, string name, CancellationToken ct = default)
    {
        var clean = Text.Clean(name, 24);
        if (clean.Length == 0)
            throw new AxoException("emotes.nameRequired");
        await _store.UpdateAsync(doc =>
        {
            var item = doc.Emotes.FirstOrDefault(e => e.Id == id) ?? throw new AxoException("errors.notFound");
            item.Name = clean;
        }, ct);
        events.Publish("emotes.changed");
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        EmoteItem? removed = null;
        await _store.UpdateAsync(doc =>
        {
            removed = doc.Emotes.FirstOrDefault(e => e.Id == id);
            doc.Emotes.RemoveAll(e => e.Id == id);
            if (doc.Slots != null)
                for (var i = 0; i < doc.Slots.Count; i++)
                    if (doc.Slots[i] == id)
                        doc.Slots[i] = null;
        }, ct);
        if (removed != null && System.IO.File.Exists(FilePath(removed)))
            System.IO.File.Delete(FilePath(removed));
        events.Publish("emotes.changed");
    }

    /// <summary>Animation eines eigenen Emotes (kompaktes JSON) samt Name der Animation darin, oder <c>null</c>.</summary>
    public async Task<(string Json, string? Animation)?> AnimationAsync(string id, CancellationToken ct = default)
    {
        var doc = await _store.LoadAsync(ct);
        var item = doc.Emotes.FirstOrDefault(e => e.Id == id);
        if (item == null || !System.IO.File.Exists(FilePath(item)))
            return null;
        return (await System.IO.File.ReadAllTextAsync(FilePath(item), ct), item.Animation);
    }

    /// <summary>Alle eigenen Emotes samt Animation – so bekommt sie die Mod.</summary>
    public async Task<IReadOnlyList<(EmoteItem Item, string Json)>> AllWithAnimationsAsync(CancellationToken ct = default)
    {
        var doc = await _store.LoadAsync(ct);
        var result = new List<(EmoteItem, string)>();
        foreach (var item in doc.Emotes.Where(e => System.IO.File.Exists(FilePath(e))))
            result.Add((item, await System.IO.File.ReadAllTextAsync(FilePath(item), ct)));
        return result;
    }

    private string FilePath(EmoteItem item) => PathGuard.Combine(paths.Emotes, item.File);

    /// <summary>Genau 8 Plätze, nur bekannte Emotes, keines doppelt.</summary>
    private static IReadOnlyList<string?> Normalize(IReadOnlyList<string?>? slots, IEnumerable<EmoteItem> imported)
    {
        if (slots == null)
            return DefaultSlots;
        var known = BuiltIn.Select(b => b.Id).Concat(imported.Select(e => e.Id)).ToHashSet();
        var seen = new HashSet<string>();
        var result = new string?[SlotCount];
        for (var i = 0; i < SlotCount && i < slots.Count; i++)
            if (slots[i] is { } id && known.Contains(id) && seen.Add(id))
                result[i] = id;
        return result;
    }

    /// <summary>"animation.model.happy_dance" → "Happy dance".</summary>
    private static string NiceName(string key, string? fallback)
    {
        if (fallback is { Length: > 0 })
            return fallback;
        var last = key.Split('.').Last().Replace('_', ' ').Replace('-', ' ').Trim();
        if (last.Length == 0)
            return "Emote";
        return Text.Clean(char.ToUpperInvariant(last[0]) + last[1..], 24);
    }
}

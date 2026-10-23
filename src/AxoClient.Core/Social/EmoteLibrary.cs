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

    /// <summary>Läuft dauerhaft (Schleife oder letzte Pose halten), bis man schleicht oder sich bewegt.</summary>
    public bool Loop { get; set; }

    /// <summary>Man darf dabei laufen; nur Schleichen beendet es.</summary>
    public bool Moving { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmoteLibraryDocument : IVersionedDocument
{
    public int FormatVersion { get; set; } = 1;
    public List<EmoteItem> Emotes { get; set; } = [];

    /// <summary>Belegung des Emote-Rads (8 Plätze, im Uhrzeigersinn ab oben): Emote-ID oder <c>null</c>.</summary>
    public List<string?>? Slots { get; set; }
}

public sealed record EmoteView(string Id, string Name, string Source, DateTimeOffset CreatedAt, bool Loop, bool Moving);

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
            .Select(View).ToList();
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
            var (loop, moving) = Detect(animation);
            added.Add(new EmoteItem { Id = id, Name = name is { Length: > 0 } ? name : "Emote", File = id + ".json", Animation = key, Loop = loop, Moving = moving });
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
        return added.Select(View).ToList();
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

    /// <summary>
    /// Stellt ein, ob das Emote dauerhaft läuft und ob man dabei laufen darf. "Dauerhaft" steht auch in der Animation selbst,
    /// damit andere Spieler es genauso sehen.
    /// </summary>
    public async Task SetOptionsAsync(string id, bool loop, bool moving, CancellationToken ct = default)
    {
        var doc = await _store.LoadAsync(ct);
        var item = doc.Emotes.FirstOrDefault(e => e.Id == id) ?? throw new AxoException("errors.notFound");
        var path = FilePath(item);
        if (System.IO.File.Exists(path) && JsonNode.Parse(await System.IO.File.ReadAllTextAsync(path, ct)) is JsonObject root)
        {
            ApplyLoop(root, loop);
            await System.IO.File.WriteAllTextAsync(path, root.ToJsonString(Json.Compact), ct);
        }
        await _store.UpdateAsync(d =>
        {
            var stored = d.Emotes.FirstOrDefault(e => e.Id == id) ?? throw new AxoException("errors.notFound");
            stored.Loop = loop;
            stored.Moving = moving;
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

    private static string? _builtInJson;

    /// <summary>Animationen der eingebauten Emotes (Bedrock-JSON aus der Mod, als Ressource eingebettet), oder <c>null</c>.</summary>
    public static string? BuiltInAnimations()
    {
        if (_builtInJson != null)
            return _builtInJson;
        using var stream = typeof(EmoteLibrary).Assembly.GetManifestResourceStream("emotes/builtin.json");
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return _builtInJson = reader.ReadToEnd();
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

    private static EmoteView View(EmoteItem e) => new(e.Id, e.Name, "imported", e.CreatedAt, e.Loop, e.Moving);

    /// <summary>
    /// Liest aus der Animation, ob sie dauerhaft läuft (Bedrock <c>loop</c>, Emotecraft <c>isLoop</c>) und ob man dabei
    /// laufen kann (keine Bein-Bewegung). Die Datei enthält genau eine Animation.
    /// </summary>
    internal static (bool Loop, bool Moving) Detect(JsonObject animation)
    {
        static bool Truthy(JsonNode? node) => node is JsonValue v && (
            v.TryGetValue<bool>(out var b) && b
            || v.TryGetValue<string>(out var s) && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "hold_on_last_frame"));
        static bool IsLeg(string bone) => bone.Contains("leg", StringComparison.OrdinalIgnoreCase);

        if (animation["animations"] is JsonObject animations)
        {
            var anims = animations.Select(a => a.Value).OfType<JsonObject>().ToList();
            var loop = anims.Any(a => Truthy(a["loop"]));
            var legs = anims.Any(a => a["bones"] is JsonObject bones && bones.Any(b => IsLeg(b.Key)));
            return (loop, !legs);
        }
        if (animation["emote"] is JsonObject emote)
        {
            var loop = Truthy(emote["isLoop"]);
            var legs = emote["moves"] is JsonArray moves && moves.OfType<JsonObject>().Any(m => m.Any(p => IsLeg(p.Key)));
            return (loop, !legs);
        }
        return (false, false);
    }

    /// <summary>Schreibt "dauerhaft" in die Animation. Eine gehaltene letzte Pose (<c>hold_on_last_frame</c>) bleibt erhalten.</summary>
    internal static void ApplyLoop(JsonObject animation, bool loop)
    {
        if (animation["animations"] is JsonObject animations)
        {
            foreach (var anim in animations.Select(a => a.Value).OfType<JsonObject>())
            {
                var hold = anim["loop"] is JsonValue v && v.TryGetValue<string>(out var s) && s == "hold_on_last_frame";
                if (!loop)
                    anim.Remove("loop");
                else if (!hold)
                    anim["loop"] = true;
            }
        }
        else if (animation["emote"] is JsonObject emote)
        {
            emote["isLoop"] = loop;
        }
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

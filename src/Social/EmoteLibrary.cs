using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AxoClient.Common;
using AxoClient.DataModel.Social;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// Eigene Emotes (importierte Animationen) und die Belegung des Emote-Rads. Die eingebauten Emotes liefert die AxoMod
    /// selbst; der Launcher kennt nur ihre IDs und Namen für die Anzeige.
    /// </summary>
    public sealed class EmoteLibrary : IEmoteLibrary
    {
        private readonly AppPaths _paths;
        private readonly IEventHub _events;
        private readonly ITextHelper _textHelper;
        private readonly IJsonHelper _jsonHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        public const int SlotCount = 8;

        /// <summary>Höchstgröße der Animation (kompaktes JSON) – so viel darf der Live-Raum weiterreichen.</summary>
        public const int MaxAnimationChars = 200_000;

        private const int MaxFileBytes = 2 * 1024 * 1024;
        private const int MaxEmotes = 60;

        /// <summary>Eingebaute Emotes der AxoMod (ID, Name) in der Reihenfolge der Mod.</summary>
        /// <summary>Eingebaute Emotes der Mod (Id und Name).</summary>
        public IReadOnlyList<(string Id, string Name)> BuiltIn { get; } =
        [
            ("wave", "Winken"), ("bow", "Verbeugen"), ("clap", "Klatschen"), ("cheer", "Jubeln"), ("dance", "Tanzen"),
            ("sit", "Hinsetzen"), ("point", "Zeigen"), ("salute", "Salutieren"), ("facepalm", "Facepalm"), ("nod", "Nicken")
        ];

        private readonly IReadOnlyList<string?> _defaultSlots = ["wave", "cheer", "dance", "clap", "sit", "point", "bow", null];

        private readonly JsonStore<EmoteLibraryDocument> _store;

        public async Task<IReadOnlyList<EmoteView>> List(CancellationToken cancellationToken = default)
        {
            var document = await _store.Load(cancellationToken);
            return document.Emotes.Where(emote => System.IO.File.Exists(filePath(emote)))
                .Select(view).ToList();
        }

        public async Task<IReadOnlyList<string?>> Slots(CancellationToken cancellationToken = default)
        {
            var document = await _store.Load(cancellationToken);
            return normalize(document.Slots, document.Emotes);
        }

        public async Task SetSlots(IReadOnlyList<string?> slots, CancellationToken cancellationToken = default)
        {
            await _store.Update(document => document.Slots = normalize(slots, document.Emotes).ToList(), cancellationToken);
            _events.Publish("emotes.changed");
        }

        /// <summary>Importiert eine Animationsdatei (Blockbench/GeckoLib-Bedrock oder PlayerAnimator/Emotecraft). Gibt die neuen Emotes zurück.</summary>
        public async Task<IReadOnlyList<EmoteView>> Import(string sourcePath, CancellationToken cancellationToken = default)
        {
            var file = new FileInfo(sourcePath);
            if (!file.Exists || file.Length > MaxFileBytes)
            {
                throw new AxoException("emotes.tooLarge");
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(sourcePath, cancellationToken)) as JsonObject
                       ?? throw new AxoException("emotes.invalidFile");
            }
            catch (JsonException)
            {
                throw new AxoException("emotes.invalidFile");
            }

            var baseName = _textHelper.Clean(Path.GetFileNameWithoutExtension(sourcePath), 24);
            var found = new List<(string Name, string? Key, JsonObject Animation)>();
            if (root["animations"] is JsonObject animations && animations.Count > 0)
            {
                // Bedrock/GeckoLib: jede Animation ein eigenes Emote, mit nur dieser Animation in der Datei
                foreach (var (key, value) in animations)
                {
                    if (value is not JsonObject)
                    {
                        continue;
                    }

                    var single = new JsonObject
                    {
                        ["format_version"] = root["format_version"]?.DeepClone() ?? "1.8.0",
                        ["animations"] = new JsonObject { [key] = value.DeepClone() }
                    };
                    found.Add((niceName(key, animations.Count == 1 ? baseName : null), key, single));
                }
            }
            else if (root["emote"] is JsonObject)
            {
                // PlayerAnimator/Emotecraft: eine Animation je Datei
                var name = root["name"]?.GetValueKind() == JsonValueKind.String ? _textHelper.Clean(root["name"]!.GetValue<string>(), 24) : baseName;
                found.Add((name is { Length: > 0 } ? name : baseName, null, (JsonObject)root.DeepClone()));
            }
            if (found.Count == 0)
            {
                throw new AxoException("emotes.invalidFile");
            }

            Directory.CreateDirectory(_paths.Emotes);
            var added = new List<EmoteItem>();
            foreach (var (name, key, animation) in found)
            {
                var compact = animation.ToJsonString(_jsonHelper.Compact);
                if (compact.Length > MaxAnimationChars)
                {
                    throw new AxoException("emotes.tooLarge");
                }

                var id = "c_" + _textHelper.Sha256Hex(Encoding.UTF8.GetBytes(compact))[..12];
                await System.IO.File.WriteAllTextAsync(Path.Combine(_paths.Emotes, id + ".json"), compact, cancellationToken);
                var (loop, moving) = Detect(animation);
                added.Add(new EmoteItem { Id = id, Name = name is { Length: > 0 } ? name : "Emote", File = id + ".json", Animation = key, Loop = loop, Moving = moving });
            }

            await _store.Update(document =>
            {
                foreach (var item in added)
                {
                    document.Emotes.RemoveAll(emote => emote.Id == item.Id);
                    document.Emotes.Insert(0, item);
                }
                if (document.Emotes.Count > MaxEmotes)
                {
                    throw new AxoException("emotes.tooMany");
                }
            }, cancellationToken);
            _events.Publish("emotes.changed");
            return added.Select(view).ToList();
        }

        public async Task Rename(string id, string name, CancellationToken cancellationToken = default)
        {
            var clean = _textHelper.Clean(name, 24);
            if (clean.Length == 0)
            {
                throw new AxoException("emotes.nameRequired");
            }

            await _store.Update(document =>
            {
                var item = document.Emotes.FirstOrDefault(emote => emote.Id == id) ?? throw new AxoException("errors.notFound");
                item.Name = clean;
            }, cancellationToken);
            _events.Publish("emotes.changed");
        }

        /// <summary>
        /// Stellt ein, ob das Emote dauerhaft läuft und ob man dabei laufen darf. "Dauerhaft" steht auch in der Animation selbst,
        /// damit andere Spieler es genauso sehen.
        /// </summary>
        public async Task SetOptions(string id, bool loop, bool moving, CancellationToken cancellationToken = default)
        {
            var document = await _store.Load(cancellationToken);
            var item = document.Emotes.FirstOrDefault(emote => emote.Id == id) ?? throw new AxoException("errors.notFound");
            var path = filePath(item);
            if (System.IO.File.Exists(path) && JsonNode.Parse(await System.IO.File.ReadAllTextAsync(path, cancellationToken)) is JsonObject root)
            {
                ApplyLoop(root, loop);
                await System.IO.File.WriteAllTextAsync(path, root.ToJsonString(_jsonHelper.Compact), cancellationToken);
            }
            await _store.Update(emoteLibraryDocument =>
            {
                var stored = emoteLibraryDocument.Emotes.FirstOrDefault(emote => emote.Id == id) ?? throw new AxoException("errors.notFound");
                stored.Loop = loop;
                stored.Moving = moving;
            }, cancellationToken);
            _events.Publish("emotes.changed");
        }

        public async Task Remove(string id, CancellationToken cancellationToken = default)
        {
            EmoteItem? removed = null;
            await _store.Update(document =>
            {
                removed = document.Emotes.FirstOrDefault(emote => emote.Id == id);
                document.Emotes.RemoveAll(emote => emote.Id == id);
                if (document.Slots != null)
                {
                    for (var index = 0; index < document.Slots.Count; index++)
                    {
                        if (document.Slots[index] == id)
                        {
                            document.Slots[index] = null;
                        }
                    }
                }
            }, cancellationToken);
            if (removed != null && System.IO.File.Exists(filePath(removed)))
            {
                System.IO.File.Delete(filePath(removed));
            }

            _events.Publish("emotes.changed");
        }

        private string? _builtInJson;

        public EmoteLibrary(
            AppPaths paths,
            IEventHub events,
            ITextHelper textHelper,
            IJsonHelper jsonHelper,
            IPathGuard pathGuard,
            IJsonStoreFactory jsonStoreFactory)
        {
            _paths = paths;
            _events = events;
            _textHelper = textHelper;
            _jsonHelper = jsonHelper;
            _pathGuard = pathGuard;
            _jsonStoreFactory = jsonStoreFactory;
            _store = _jsonStoreFactory.Create<EmoteLibraryDocument>(paths.Fs, paths.EmoteLibrary);
        }

        /// <summary>Animationen der eingebauten Emotes (Bedrock-JSON aus der Mod, als Ressource eingebettet), oder <c>null</c>.</summary>
        public string? BuiltInAnimations()
        {
            if (_builtInJson != null)
            {
                return _builtInJson;
            }

            using var stream = typeof(EmoteLibrary).Assembly.GetManifestResourceStream("emotes/builtin.json");
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return _builtInJson = reader.ReadToEnd();
        }

        /// <summary>Animation eines eigenen Emotes (kompaktes JSON) samt Name der Animation darin, oder <c>null</c>.</summary>
        public async Task<(string Json, string? Animation)?> Animation(string id, CancellationToken cancellationToken = default)
        {
            var document = await _store.Load(cancellationToken);
            var item = document.Emotes.FirstOrDefault(emote => emote.Id == id);
            if (item == null || !System.IO.File.Exists(filePath(item)))
            {
                return null;
            }

            return (await System.IO.File.ReadAllTextAsync(filePath(item), cancellationToken), item.Animation);
        }

        /// <summary>Alle eigenen Emotes samt Animation – so bekommt sie die Mod.</summary>
        public async Task<IReadOnlyList<(EmoteItem Item, string Json)>> AllWithAnimations(CancellationToken cancellationToken = default)
        {
            var document = await _store.Load(cancellationToken);
            var result = new List<(EmoteItem, string)>();
            foreach (var item in document.Emotes.Where(emote => System.IO.File.Exists(filePath(emote))))
            {
                result.Add((item, await System.IO.File.ReadAllTextAsync(filePath(item), cancellationToken)));
            }

            return result;
        }

        private EmoteView view(EmoteItem emote) => new(emote.Id, emote.Name, "imported", emote.CreatedAt, emote.Loop, emote.Moving);

        /// <summary>
        /// Liest aus der Animation, ob sie dauerhaft läuft (Bedrock <c>loop</c>, Emotecraft <c>isLoop</c>) und ob man dabei
        /// laufen kann (keine Bein-Bewegung). Die Datei enthält genau eine Animation.
        /// </summary>
        internal (bool Loop, bool Moving) Detect(JsonObject animation)
        {
            bool Truthy(JsonNode? node) => node is JsonValue jsonValue && (
                jsonValue.TryGetValue<bool>(out var value) && value
                || jsonValue.TryGetValue<string>(out var text) && (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "hold_on_last_frame"));
            bool IsLeg(string bone) => bone.Contains("leg", StringComparison.OrdinalIgnoreCase);

            if (animation["animations"] is JsonObject animations)
            {
                var otherAnimations = animations.Select(animationPair => animationPair.Value).OfType<JsonObject>().ToList();
                var loop = otherAnimations.Any(animationObject => Truthy(animationObject["loop"]));
                var legs = otherAnimations.Any(animationObject => animationObject["bones"] is JsonObject bones && bones.Any(bone => IsLeg(bone.Key)));
                return (loop, !legs);
            }
            if (animation["emote"] is JsonObject emote)
            {
                var loop = Truthy(emote["isLoop"]);
                var legs = emote["moves"] is JsonArray moves && moves.OfType<JsonObject>().Any(move => move.Any(pair => IsLeg(pair.Key)));
                return (loop, !legs);
            }
            return (false, false);
        }

        /// <summary>Schreibt "dauerhaft" in die Animation. Eine gehaltene letzte Pose (<c>hold_on_last_frame</c>) bleibt erhalten.</summary>
        internal void ApplyLoop(JsonObject animation, bool loop)
        {
            if (animation["animations"] is JsonObject animations)
            {
                foreach (var otherAnimation in animations.Select(animationPair => animationPair.Value).OfType<JsonObject>())
                {
                    var hold = otherAnimation["loop"] is JsonValue loopValue && loopValue.TryGetValue<string>(out var value) && value == "hold_on_last_frame";
                    if (!loop)
                    {
                        otherAnimation.Remove("loop");
                    }
                    else if (!hold)
                    {
                        otherAnimation["loop"] = true;
                    }
                }
            }
            else if (animation["emote"] is JsonObject emote)
            {
                emote["isLoop"] = loop;
            }
        }

        private string filePath(EmoteItem item) => _pathGuard.Combine(_paths.Emotes, item.File);

        /// <summary>Genau 8 Plätze, nur bekannte Emotes, keines doppelt.</summary>
        private IReadOnlyList<string?> normalize(IReadOnlyList<string?>? slots, IEnumerable<EmoteItem> imported)
        {
            if (slots == null)
            {
                return _defaultSlots;
            }

            var known = BuiltIn.Select(builtInEmote => builtInEmote.Id).Concat(imported.Select(emote => emote.Id)).ToHashSet();
            var seen = new HashSet<string>();
            var result = new string?[SlotCount];
            for (var index = 0; index < SlotCount && index < slots.Count; index++)
            {
                if (slots[index] is { } id && known.Contains(id) && seen.Add(id))
                {
                    result[index] = id;
                }
            }

            return result;
        }

        /// <summary>"animation.model.happy_dance" → "Happy dance".</summary>
        private string niceName(string key, string? fallback)
        {
            if (fallback is { Length: > 0 })
            {
                return fallback;
            }

            var last = key.Split('.').Last().Replace('_', ' ').Replace('-', ' ').Trim();
            if (last.Length == 0)
            {
                return "Emote";
            }

            return _textHelper.Clean(char.ToUpperInvariant(last[0]) + last[1..], 24);
        }
    }
}

using System.IO.Compression;
using System.Text.Json;
using AxoClient.Core.Common;

namespace AxoClient.Core.Content;

/// <summary>Liest Mod-ID, Abhängigkeiten, Konflikte und Pakete aus einer Mod-Jar (Fabric, Quilt, Forge, NeoForge).</summary>
public static class ModMetadataReader
{
    private const long MaxMetaBytes = 1024 * 1024;

    private static readonly HashSet<string> IgnoredDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "forge", "neoforge", "fml", "javafml",
        "mixinextras"
    };

    public sealed record Result(ModMetadata Meta, string? Name, string? Version, string? Author);

    /// <param name="preferLoader">
    /// Enthält die Jar Metadaten für mehrere Loader (z. B. Collective: Fabric und NeoForge), werden die für diesen Loader
    /// gelesen ("fabric", "quilt", "forge", "neoforge"). <see cref="ModMetadata.Loaders"/> nennt immer alle.
    /// </param>
    public static Result? Read(string jarPath, string? preferLoader = null)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            return Read(zip, preferLoader);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException)
        {
            return null;
        }
    }

    public static Result? Read(ZipArchive zip, string? preferLoader = null)
    {
        // Alle Metadaten der Jar, in der üblichen Reihenfolge
        var variants = new List<(string Loader, Func<Result?> Parse)>();
        if (Entry(zip, "fabric.mod.json") is { } fabric)
            variants.Add(("fabric", () => ReadFabric(fabric)));
        if (Entry(zip, "quilt.mod.json") is { } quilt)
            variants.Add(("quilt", () => ReadQuilt(quilt)));
        if (Entry(zip, "META-INF/neoforge.mods.toml") is { } neo)
            variants.Add(("neoforge", () => ReadToml(neo, "neoforge")));
        if (Entry(zip, "META-INF/mods.toml") is { } forge)
            variants.Add(("forge", () => ReadToml(forge, "forge")));
        if (variants.Count == 0 && Entry(zip, "mcmod.info") is { } legacy)
            variants.Add(("forge", () => ReadMcmodInfo(legacy)));

        // Quilt lädt auch Fabric-Mods; Forge und NeoForge lesen notfalls die toml des anderen (gleicher Aufbau)
        string[] wanted = preferLoader switch
        {
            "quilt" => ["quilt", "fabric"],
            "forge" => ["forge", "neoforge"],
            "neoforge" => ["neoforge", "forge"],
            null => [],
            _ => [preferLoader]
        };
        var chosen = wanted.Select(w => variants.FirstOrDefault(v => v.Loader == w)).FirstOrDefault(v => v.Parse != null);
        if (chosen.Parse == null && variants.Count > 0)
            chosen = variants[0];
        var result = chosen.Parse?.Invoke();
        if (result != null)
            result.Meta.Loaders = result.Meta.Loaders.Concat(variants.Select(v => v.Loader)).Concat(ServiceLoaders(zip)).Distinct().ToList();

        if (result == null)
        {
            // Reine Container-Jar (z. B. Kotlin for Forge): keine eigenen Metadaten, die Mods stecken in META-INF/jarjar.
            // Ohne diese IDs gälte z. B. "kotlinforforge" als fehlend, obwohl es installiert ist.
            var nestedOnly = NestedIds(zip).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (nestedOnly.Count == 0)
                return null;
            var container = new ModMetadata { Format = ModMetadata.CurrentFormat, Provides = nestedOnly };
            container.Packages = Packages(zip);
            return new Result(container, null, null, null);
        }
        result.Meta.Format = ModMetadata.CurrentFormat;
        result.Meta.ModVersion = result.Version;
        result.Meta.Packages = Packages(zip);
        result.Meta.Provides.AddRange(NestedIds(zip));
        if (result.Meta.Mixins.Count == 0)
            result.Meta.Mixins = zip.Entries.Where(e => !e.FullName.Contains('/') && e.Name.EndsWith(".mixins.json"))
                .Select(e => e.Name).Take(20).ToList();
        return result;
    }

    /// <summary>
    /// Loader, bei denen sich die Jar nur über Java-Services anmeldet (z. B. Preloading Tricks: Forge-Mod-Locator ohne
    /// eigene mods.toml). Ohne das gälte so eine Bibliothek in einer Forge-Instanz als falscher Loader.
    /// </summary>
    private static IEnumerable<string> ServiceLoaders(ZipArchive zip)
    {
        const string services = "META-INF/services/";
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(services, StringComparison.Ordinal)))
        {
            var name = entry.FullName[services.Length..];
            if (name.StartsWith("net.minecraftforge.", StringComparison.Ordinal))
                yield return "forge";
            else if (name.StartsWith("net.neoforged.", StringComparison.Ordinal))
                yield return "neoforge";
        }
    }

    private static string? Entry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry == null || entry.Length > MaxMetaBytes)
            return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static Result ReadFabric(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var meta = new ModMetadata
        {
            ModId = Str(root, "id"),
            Loaders = ["fabric"],
            Depends = Keys(root, "depends"),
            Breaks = Keys(root, "breaks"),
            Conflicts = Keys(root, "conflicts"),
            Provides = root.TryGetProperty("provides", out var provides) && provides.ValueKind == JsonValueKind.Array
                ? provides.EnumerateArray().Select(p => p.GetString()).OfType<string>().ToList()
                : []
        };
        if (root.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Object)
        {
            meta.MinecraftRange = RangeText(depends, "minecraft");
            meta.LoaderRange = RangeText(depends, "fabricloader");
        }
        meta.BreakRanges = Ranges(root, "breaks");
        meta.ConflictRanges = Ranges(root, "conflicts");
        if (root.TryGetProperty("mixins", out var mixins) && mixins.ValueKind == JsonValueKind.Array)
            meta.Mixins = mixins.EnumerateArray()
                .Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : Str(m, "config"))
                .OfType<string>().Take(20).ToList();
        var author = root.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0
            ? authors[0].ValueKind == JsonValueKind.String ? authors[0].GetString() : Str(authors[0], "name")
            : null;
        return new Result(meta, Str(root, "name"), Str(root, "version"), author);
    }

    private static Result ReadQuilt(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var loader = doc.RootElement.TryGetProperty("quilt_loader", out var q) ? q : default;
        var meta = new ModMetadata { ModId = loader.ValueKind == JsonValueKind.Object ? Str(loader, "id") : null, Loaders = ["quilt"] };
        if (loader.ValueKind == JsonValueKind.Object && loader.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in deps.EnumerateArray())
            {
                var id = d.ValueKind == JsonValueKind.String ? d.GetString() : Str(d, "id");
                var optional = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("optional", out var o) && o.GetBoolean();
                if (id == "minecraft")
                    meta.MinecraftRange = d.ValueKind == JsonValueKind.Object ? Str(d, "versions") : null;
                else if (id != null && !optional && !IgnoredDependencies.Contains(id))
                    meta.Depends.Add(id);
            }
        }
        var metadata = loader.ValueKind == JsonValueKind.Object && loader.TryGetProperty("metadata", out var md) ? md : default;
        return new Result(meta, metadata.ValueKind == JsonValueKind.Object ? Str(metadata, "name") : null,
            loader.ValueKind == JsonValueKind.Object ? Str(loader, "version") : null, null);
    }

    private static Result ReadToml(string toml, string loaderName)
    {
        var tables = MiniToml.Parse(toml);
        var mod = tables.FirstOrDefault(t => t.Name == "mods");
        var modId = mod?.Get("modId");
        var meta = new ModMetadata { ModId = modId, Loaders = [loaderName] };
        // Eine Jar kann mehrere Mods enthalten ([[mods]] mehrfach): die übrigen stellt sie ebenfalls bereit
        var ownIds = tables.Where(t => t.Name == "mods").Select(t => t.Get("modId")).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        meta.Provides.AddRange(ownIds.Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)));
        foreach (var dep in tables.Where(t => t.Name.StartsWith("dependencies.", StringComparison.Ordinal)))
        {
            var id = dep.Get("modId");
            if (id == null || ownIds.Contains(id))
                continue;
            // Nur auf dem Server nötig: für den Client keine Abhängigkeit
            if (string.Equals(dep.Get("side"), "SERVER", StringComparison.OrdinalIgnoreCase))
                continue;
            var type = dep.Get("type")?.ToLowerInvariant();
            var mandatory = dep.Get("mandatory") == "true" || type == "required";
            // Stecken mehrere Mods in der Jar, gilt der Bereich der Haupt-Mod (sonst zählt, was zuletzt kam)
            var primary = modId != null && dep.Name.Equals($"dependencies.{modId}", StringComparison.OrdinalIgnoreCase);
            if (id == "minecraft")
            {
                if (primary || meta.MinecraftRange == null)
                    meta.MinecraftRange = dep.Get("versionRange");
            }
            else if (id is "forge" or "neoforge")
            {
                if (primary || meta.LoaderRange == null)
                    meta.LoaderRange = dep.Get("versionRange");
            }
            else if (type == "incompatible")
            {
                meta.Breaks.Add(id);
                if (dep.Get("versionRange") is { Length: > 0 } range)
                    meta.BreakRanges[id] = range;
            }
            else if (type == "discouraged")
            {
                meta.Conflicts.Add(id);
                if (dep.Get("versionRange") is { Length: > 0 } range)
                    meta.ConflictRanges[id] = range;
            }
            else if (mandatory && !IgnoredDependencies.Contains(id))
                meta.Depends.Add(id);
        }
        meta.Mixins = tables.Where(t => t.Name == "mixins").Select(t => t.Get("config")).OfType<string>().ToList();
        return new Result(meta, mod?.Get("displayName"), mod?.Get("version") is { } v && !v.Contains("${") ? v : null, mod?.Get("authors"));
    }

    private static Result? ReadMcmodInfo(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        var first = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0]
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("modList", out var list) && list.GetArrayLength() > 0 ? list[0]
            : default;
        if (first.ValueKind != JsonValueKind.Object)
            return null;
        return new Result(new ModMetadata
        {
            ModId = Str(first, "modid"),
            Loaders = ["forge"],
            MinecraftRange = Str(first, "mcversion")
        }, Str(first, "name"), Str(first, "version"), null);
    }

    /// <summary>Mod-IDs aus mitgelieferten Jars (Fabric: META-INF/jars, Forge/NeoForge: META-INF/jarjar).</summary>
    private static IEnumerable<string> NestedIds(ZipArchive zip)
    {
        var ids = new List<string>();
        foreach (var entry in zip.Entries.Where(e => (e.FullName.StartsWith("META-INF/jars/") || e.FullName.StartsWith("META-INF/jarjar/"))
                                                     && e.Name.EndsWith(".jar") && e.Length < 32 * 1024 * 1024).Take(200))
        {
            try
            {
                using var buffer = new MemoryStream();
                using (var stream = entry.Open())
                    stream.CopyTo(buffer);
                buffer.Position = 0;
                using var nested = new ZipArchive(buffer, ZipArchiveMode.Read);
                if (Read(nested) is { Meta.ModId: { } id } inner)
                {
                    ids.Add(id);
                    ids.AddRange(inner.Meta.Provides);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException)
            {
            }
        }
        return ids;
    }

    /// <summary>Java-Pakete der Jar, auf drei Ebenen gekürzt (z. B. "me.jellysquid.mods").</summary>
    private static List<string> Packages(ZipArchive zip) =>
        zip.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal) && !e.FullName.StartsWith("META-INF/"))
            .Select(e => e.FullName.Split('/'))
            .Where(parts => parts.Length > 2)
            .Select(parts => string.Join('.', parts.Take(Math.Min(3, parts.Length - 1))))
            .Distinct()
            .Take(30)
            .ToList();

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Keys(JsonElement root, string name) =>
        root.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object
            ? obj.EnumerateObject().Select(p => p.Name).Where(n => !IgnoredDependencies.Contains(n)).ToList()
            : [];

    private static Dictionary<string, string> Ranges(JsonElement root, string name) =>
        root.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object
            ? obj.EnumerateObject().Select(p => (p.Name, Range: RangeText(obj, p.Name)))
                .Where(p => p.Range is { Length: > 0 })
                .ToDictionary(p => p.Name, p => p.Range!, StringComparer.OrdinalIgnoreCase)
            : [];

    private static string? RangeText(JsonElement depends, string key) =>
        depends.TryGetProperty(key, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString()
            : v.ValueKind == JsonValueKind.Array ? string.Join(" || ", v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()))
            : null
            : null;

    /// <summary>Gerade genug TOML für mods.toml: Tabellen-Arrays, Schlüssel = Wert (Strings, Booleans, Zahlen).</summary>
    internal static class MiniToml
    {
        public sealed class Table(string name)
        {
            public string Name { get; } = name;
            public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? Get(string key) => Values.GetValueOrDefault(key);
        }

        public static List<Table> Parse(string text)
        {
            var tables = new List<Table>();
            var current = new Table("");
            tables.Add(current);
            string? multiKey = null;
            var multi = new System.Text.StringBuilder();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (multiKey != null)
                {
                    var end = line.IndexOf("'''", StringComparison.Ordinal);
                    if (end < 0)
                        end = line.IndexOf("\"\"\"", StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        multi.Append(line[..end]);
                        current.Values[multiKey] = multi.ToString();
                        multiKey = null;
                    }
                    else
                        multi.AppendLine(line);
                    continue;
                }
                var trimmed = StripComment(line).Trim();
                if (trimmed.Length == 0)
                    continue;
                if (trimmed.StartsWith("[[") && trimmed.EndsWith("]]"))
                {
                    current = new Table(trimmed[2..^2].Trim());
                    tables.Add(current);
                    continue;
                }
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    current = new Table(trimmed[1..^1].Trim());
                    tables.Add(current);
                    continue;
                }
                var eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = trimmed[..eq].Trim().Trim('"');
                var value = trimmed[(eq + 1)..].Trim();
                if (value.StartsWith("'''") || value.StartsWith("\"\"\""))
                {
                    var rest = value[3..];
                    var close = rest.IndexOf(value[..3], StringComparison.Ordinal);
                    if (close >= 0)
                        current.Values[key] = rest[..close];
                    else
                    {
                        multiKey = key;
                        multi.Clear().AppendLine(rest);
                    }
                    continue;
                }
                current.Values[key] = Unquote(value);
            }
            return tables;
        }

        private static string StripComment(string line)
        {
            var inString = false;
            var quote = '\0';
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inString)
                {
                    if (c == quote)
                        inString = false;
                }
                else if (c is '"' or '\'')
                {
                    inString = true;
                    quote = c;
                }
                else if (c == '#')
                    return line[..i];
            }
            return line;
        }

        private static string Unquote(string value) =>
            value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
                ? value[1..^1]
                : value;
    }
}

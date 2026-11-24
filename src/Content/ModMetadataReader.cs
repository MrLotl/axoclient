using System.IO.Compression;
using System.Text.Json;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;

namespace AxoClient.Content
{
    /// <summary>Liest Mod-ID, Abhängigkeiten, Konflikte und Pakete aus einer Mod-Jar (Fabric, Quilt, Forge, NeoForge).</summary>
    public class ModMetadataReader : IModMetadataReader
    {
        private const long MaxMetaBytes = 1024 * 1024;
        private readonly MiniToml _miniToml = new();

        private readonly HashSet<string> _ignoredDependencies = new(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "forge", "neoforge", "fml", "javafml",
            "mixinextras"
        };

        /// <param name="preferLoader">
        /// Enthält die Jar Metadaten für mehrere Loader (z. B. Collective: Fabric und NeoForge), werden die für diesen Loader
        /// gelesen ("fabric", "quilt", "forge", "neoforge"). <see cref="ModMetadata.Loaders"/> nennt immer alle.
        /// </param>
        public ModMetadataResult? Read(string jarPath, string? preferLoader = null)
        {
            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                return Read(zip, preferLoader);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
            {
                return null;
            }
        }

        public ModMetadataResult? Read(ZipArchive zip, string? preferLoader = null)
        {
            // Alle Metadaten der Jar, in der üblichen Reihenfolge
            var variants = new List<(string Loader, Func<ModMetadataResult?> Parse)>();
            if (entry(zip, "fabric.mod.json") is { } fabric)
            {
                variants.Add(("fabric", () => readFabric(fabric)));
            }

            if (entry(zip, "quilt.mod.json") is { } quilt)
            {
                variants.Add(("quilt", () => readQuilt(quilt)));
            }

            if (entry(zip, "META-INF/neoforge.mods.toml") is { } neoForge)
            {
                variants.Add(("neoforge", () => readToml(neoForge, "neoforge")));
            }

            if (entry(zip, "META-INF/mods.toml") is { } forge)
            {
                variants.Add(("forge", () => readToml(forge, "forge")));
            }

            if (variants.Count == 0 && entry(zip, "mcmod.info") is { } legacy)
            {
                variants.Add(("forge", () => readMcmodInfo(legacy)));
            }

            // Quilt lädt auch Fabric-Mods; Forge und NeoForge lesen notfalls die toml des anderen (gleicher Aufbau)
            string[] wanted = preferLoader switch
            {
                "quilt" => ["quilt", "fabric"],
                "forge" => ["forge", "neoforge"],
                "neoforge" => ["neoforge", "forge"],
                null => [],
                _ => [preferLoader]
            };
            var chosen = wanted.Select(wantedLoader => variants.FirstOrDefault(variant => variant.Loader == wantedLoader)).FirstOrDefault(variant => variant.Parse != null);
            if (chosen.Parse == null && variants.Count > 0)
            {
                chosen = variants[0];
            }

            var result = chosen.Parse?.Invoke();
            if (result != null)
            {
                result.Meta.Loaders = result.Meta.Loaders.Concat(variants.Select(variant => variant.Loader)).Concat(serviceLoaders(zip)).Distinct().ToList();
            }

            if (result == null)
            {
                // Reine Container-Jar (z. B. Kotlin for Forge): keine eigenen Metadaten, die Mods stecken in META-INF/jarjar.
                // Ohne diese IDs gälte z. B. "kotlinforforge" als fehlend, obwohl es installiert ist.
                var nestedOnly = nestedIds(zip).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (nestedOnly.Count == 0)
                {
                    return null;
                }

                var container = new ModMetadata { Format = ModMetadata.CurrentFormat, Provides = nestedOnly };
                container.Packages = packages(zip);
                return new ModMetadataResult(container, null, null, null);
            }
            result.Meta.Format = ModMetadata.CurrentFormat;
            result.Meta.ModVersion = result.Version;
            result.Meta.Packages = packages(zip);
            result.Meta.Provides.AddRange(nestedIds(zip));
            if (result.Meta.Mixins.Count == 0)
            {
                result.Meta.Mixins = zip.Entries.Where(zipEntry => !zipEntry.FullName.Contains('/') && zipEntry.Name.EndsWith(".mixins.json"))
                    .Select(zipEntry => zipEntry.Name).Take(20).ToList();
            }

            return result;
        }

        /// <summary>
        /// Loader, bei denen sich die Jar nur über Java-Services anmeldet (z. B. Preloading Tricks: Forge-Mod-Locator ohne
        /// eigene mods.toml). Ohne das gälte so eine Bibliothek in einer Forge-Instanz als falscher Loader.
        /// </summary>
        private IEnumerable<string> serviceLoaders(ZipArchive zip)
        {
            const string services = "META-INF/services/";
            foreach (var entry in zip.Entries.Where(zipEntry => zipEntry.FullName.StartsWith(services, StringComparison.Ordinal)))
            {
                var name = entry.FullName[services.Length..];
                if (name.StartsWith("net.minecraftforge.", StringComparison.Ordinal))
                {
                    yield return "forge";
                }
                else if (name.StartsWith("net.neoforged.", StringComparison.Ordinal))
                {
                    yield return "neoforge";
                }
            }
        }

        private string? entry(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null || entry.Length > MaxMetaBytes)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        private ModMetadataResult readFabric(string json)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            var meta = new ModMetadata
            {
                ModId = readString(root, "id"),
                Loaders = ["fabric"],
                Depends = keys(root, "depends"),
                Breaks = keys(root, "breaks"),
                Conflicts = keys(root, "conflicts"),
                Provides = root.TryGetProperty("provides", out var provides) && provides.ValueKind == JsonValueKind.Array
                    ? provides.EnumerateArray().Select(providedMod => providedMod.GetString()).OfType<string>().ToList()
                    : []
            };
            if (root.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Object)
            {
                meta.MinecraftRange = rangeText(depends, "minecraft");
                meta.LoaderRange = rangeText(depends, "fabricloader");
            }
            meta.BreakRanges = ranges(root, "breaks");
            meta.ConflictRanges = ranges(root, "conflicts");
            if (root.TryGetProperty("mixins", out var mixins) && mixins.ValueKind == JsonValueKind.Array)
            {
                meta.Mixins = mixins.EnumerateArray()
                    .Select(mixin => mixin.ValueKind == JsonValueKind.String ? mixin.GetString() : readString(mixin, "config"))
                    .OfType<string>().Take(20).ToList();
            }

            var author = root.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0
                ? authors[0].ValueKind == JsonValueKind.String ? authors[0].GetString() : readString(authors[0], "name")
                : null;
            return new ModMetadataResult(meta, readString(root, "name"), readString(root, "version"), author);
        }

        private ModMetadataResult readQuilt(string json)
        {
            using var document = JsonDocument.Parse(json);
            var loader = document.RootElement.TryGetProperty("quilt_loader", out var quiltLoader) ? quiltLoader : default;
            var meta = new ModMetadata { ModId = loader.ValueKind == JsonValueKind.Object ? readString(loader, "id") : null, Loaders = ["quilt"] };
            if (loader.ValueKind == JsonValueKind.Object && loader.TryGetProperty("depends", out var dependencies) && dependencies.ValueKind == JsonValueKind.Array)
            {
                foreach (var dependency in dependencies.EnumerateArray())
                {
                    var id = dependency.ValueKind == JsonValueKind.String ? dependency.GetString() : readString(dependency, "id");
                    var optional = dependency.ValueKind == JsonValueKind.Object && dependency.TryGetProperty("optional", out var optionalElement) && optionalElement.GetBoolean();
                    if (id == "minecraft")
                    {
                        meta.MinecraftRange = dependency.ValueKind == JsonValueKind.Object ? readString(dependency, "versions") : null;
                    }
                    else if (id != null && !optional && !_ignoredDependencies.Contains(id))
                    {
                        meta.Depends.Add(id);
                    }
                }
            }
            var metadata = loader.ValueKind == JsonValueKind.Object && loader.TryGetProperty("metadata", out var metadataElement) ? metadataElement : default;
            return new ModMetadataResult(meta, metadata.ValueKind == JsonValueKind.Object ? readString(metadata, "name") : null,
                loader.ValueKind == JsonValueKind.Object ? readString(loader, "version") : null, null);
        }

        private ModMetadataResult readToml(string toml, string loaderName)
        {
            var tables = _miniToml.Parse(toml);
            var mod = tables.FirstOrDefault(table => table.Name == "mods");
            var modId = mod?.Get("modId");
            var meta = new ModMetadata { ModId = modId, Loaders = [loaderName] };
            // Eine Jar kann mehrere Mods enthalten ([[mods]] mehrfach): die übrigen stellt sie ebenfalls bereit
            var ownIds = tables.Where(table => table.Name == "mods").Select(table => table.Get("modId")).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            meta.Provides.AddRange(ownIds.Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)));
            foreach (var dependency in tables.Where(table => table.Name.StartsWith("dependencies.", StringComparison.Ordinal)))
            {
                var id = dependency.Get("modId");
                if (id == null || ownIds.Contains(id))
                {
                    continue;
                }
                // Nur auf dem Server nötig: für den Client keine Abhängigkeit
                if (string.Equals(dependency.Get("side"), "SERVER", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var type = dependency.Get("type")?.ToLowerInvariant();
                var mandatory = dependency.Get("mandatory") == "true" || type == "required";
                // Stecken mehrere Mods in der Jar, gilt der Bereich der Haupt-Mod (sonst zählt, was zuletzt kam)
                var primary = modId != null && dependency.Name.Equals($"dependencies.{modId}", StringComparison.OrdinalIgnoreCase);
                if (id == "minecraft")
                {
                    if (primary || meta.MinecraftRange == null)
                    {
                        meta.MinecraftRange = dependency.Get("versionRange");
                    }
                }
                else if (id is "forge" or "neoforge")
                {
                    if (primary || meta.LoaderRange == null)
                    {
                        meta.LoaderRange = dependency.Get("versionRange");
                    }
                }
                else if (type == "incompatible")
                {
                    meta.Breaks.Add(id);
                    if (dependency.Get("versionRange") is { Length: > 0 } range)
                    {
                        meta.BreakRanges[id] = range;
                    }
                }
                else if (type == "discouraged")
                {
                    meta.Conflicts.Add(id);
                    if (dependency.Get("versionRange") is { Length: > 0 } range)
                    {
                        meta.ConflictRanges[id] = range;
                    }
                }
                else if (mandatory && !_ignoredDependencies.Contains(id))
                {
                    meta.Depends.Add(id);
                }
            }
            meta.Mixins = tables.Where(table => table.Name == "mixins").Select(table => table.Get("config")).OfType<string>().ToList();
            return new ModMetadataResult(meta, mod?.Get("displayName"), mod?.Get("version") is { } modVersion && !modVersion.Contains("${") ? modVersion : null, mod?.Get("authors"));
        }

        private ModMetadataResult? readMcmodInfo(string json)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = document.RootElement;
            var first = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0]
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("modList", out var list) && list.GetArrayLength() > 0 ? list[0]
                : default;
            if (first.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new ModMetadataResult(new ModMetadata
            {
                ModId = readString(first, "modid"),
                Loaders = ["forge"],
                MinecraftRange = readString(first, "mcversion")
            }, readString(first, "name"), readString(first, "version"), null);
        }

        /// <summary>Mod-IDs aus mitgelieferten Jars (Fabric: META-INF/jars, Forge/NeoForge: META-INF/jarjar).</summary>
        private IEnumerable<string> nestedIds(ZipArchive zip)
        {
            var ids = new List<string>();
            foreach (var entry in zip.Entries.Where(zipEntry => (zipEntry.FullName.StartsWith("META-INF/jars/") || zipEntry.FullName.StartsWith("META-INF/jarjar/"))
                                                         && zipEntry.Name.EndsWith(".jar") && zipEntry.Length < 32 * 1024 * 1024).Take(200))
            {
                try
                {
                    using var buffer = new MemoryStream();
                    using (var stream = entry.Open())
                    {
                        stream.CopyTo(buffer);
                    }

                    buffer.Position = 0;
                    using var nested = new ZipArchive(buffer, ZipArchiveMode.Read);
                    if (Read(nested) is { Meta.ModId: { } id } inner)
                    {
                        ids.Add(id);
                        ids.AddRange(inner.Meta.Provides);
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
                {
                }
            }
            return ids;
        }

        /// <summary>Java-Pakete der Jar, auf drei Ebenen gekürzt (z. B. "me.jellysquid.mods").</summary>
        private List<string> packages(ZipArchive zip) =>
            zip.Entries.Where(zipEntry => zipEntry.FullName.EndsWith(".class", StringComparison.Ordinal) && !zipEntry.FullName.StartsWith("META-INF/"))
                .Select(zipEntry => zipEntry.FullName.Split('/'))
                .Where(parts => parts.Length > 2)
                .Select(parts => string.Join('.', parts.Take(Math.Min(3, parts.Length - 1))))
                .Distinct()
                .Take(30)
                .ToList();

        private string? readString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var valueElement) && valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() : null;

        private List<string> keys(JsonElement root, string name) =>
            root.TryGetProperty(name, out var jsonObject) && jsonObject.ValueKind == JsonValueKind.Object
                ? jsonObject.EnumerateObject().Select(property => property.Name).Where(dependencyName => !_ignoredDependencies.Contains(dependencyName)).ToList()
                : [];

        private Dictionary<string, string> ranges(JsonElement root, string name) =>
            root.TryGetProperty(name, out var jsonObject) && jsonObject.ValueKind == JsonValueKind.Object
                ? jsonObject.EnumerateObject().Select(property => (property.Name, Range: rangeText(jsonObject, property.Name)))
                    .Where(pair => pair.Range is { Length: > 0 })
                    .ToDictionary(pair => pair.Name, pair => pair.Range!, StringComparer.OrdinalIgnoreCase)
                : [];

        private string? rangeText(JsonElement depends, string key) =>
            depends.TryGetProperty(key, out var rangeElement)
                ? rangeElement.ValueKind == JsonValueKind.String ? rangeElement.GetString()
                : rangeElement.ValueKind == JsonValueKind.Array ? string.Join(" || ", rangeElement.EnumerateArray().Where(rangePart => rangePart.ValueKind == JsonValueKind.String).Select(rangePart => rangePart.GetString()))
                : null
                : null;

        /// <summary>Gerade genug TOML für mods.toml: Tabellen-Arrays, Schlüssel = Wert (Strings, Booleans, Zahlen).</summary>
        public class MiniToml
        {
            public sealed class Table
            {
                public Table(string name)
                {
                    Name = name;
                }

                public string Name { get; }
                public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
                public string? Get(string key) => Values.GetValueOrDefault(key);
            }

            public List<Table> Parse(string text)
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
                        {
                            end = line.IndexOf("\"\"\"", StringComparison.Ordinal);
                        }

                        if (end >= 0)
                        {
                            multi.Append(line[..end]);
                            current.Values[multiKey] = multi.ToString();
                            multiKey = null;
                        }
                        else
                        {
                            multi.AppendLine(line);
                        }

                        continue;
                    }
                    var trimmed = stripComment(line).Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

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
                    var equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        continue;
                    }

                    var key = trimmed[..equalsIndex].Trim().Trim('"');
                    var value = trimmed[(equalsIndex + 1)..].Trim();
                    if (value.StartsWith("'''") || value.StartsWith("\"\"\""))
                    {
                        var rest = value[3..];
                        var close = rest.IndexOf(value[..3], StringComparison.Ordinal);
                        if (close >= 0)
                        {
                            current.Values[key] = rest[..close];
                        }
                        else
                        {
                            multiKey = key;
                            multi.Clear().AppendLine(rest);
                        }
                        continue;
                    }
                    current.Values[key] = unquote(value);
                }
                return tables;
            }

            private string stripComment(string line)
            {
                var inString = false;
                var quote = '\0';
                for (var index = 0; index < line.Length; index++)
                {
                    var character = line[index];
                    if (inString)
                    {
                        if (character == quote)
                        {
                            inString = false;
                        }
                    }
                    else if (character is '"' or '\'')
                    {
                        inString = true;
                        quote = character;
                    }
                    else if (character == '#')
                    {
                        return line[..index];
                    }
                }
                return line;
            }

            private string unquote(string value) =>
                value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
                    ? value[1..^1]
                    : value;
        }
    }
}

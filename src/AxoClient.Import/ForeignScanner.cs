using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Import;
using AxoClient.Import.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Import
{
    /// <summary>
    /// Findet Instanzen anderer Launcher an ihren Standard-Speicherorten: Modrinth App, CurseForge, Prism, offizieller
    /// Launcher, Lunar, NoRisk, Badlion, Feather, LabyMod und der alte AxoClient. Außerdem lose Ordner und entpackte ZIP-Exporte
    /// (Prism/MultiMC, CurseForge-Modpacks, AxoClient-Ordner). Es wird nur gelesen, nie etwas verändert.
    /// Das letzte Ergebnis bleibt im Speicher, damit die UI Instanzen nur per Id auswählt – Pfade verlassen C# nie.
    /// </summary>
    public sealed partial class ForeignScanner : IForeignScanner
    {
        private readonly IPathGuard _pathGuard;
        private readonly IAppLog _appLog;
        private readonly ISqliteReader _sqliteReader;
        private readonly IWorldManager _worlds;
        private readonly ConcurrentDictionary<string, ForeignInstance> _known = new();

        public ForeignScanner(IPathGuard pathGuard, IAppLog appLog, ISqliteReader sqliteReader, IWorldManager worlds)
        {
            _pathGuard = pathGuard;
            _appLog = appLog;
            _sqliteReader = sqliteReader;
            _worlds = worlds;
        }

        /// <summary>Mod-Datei (auch deaktiviert)?</summary>
        public bool IsModFile(string file)
        {
            return file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
        }

        private string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private string DotMinecraft => Path.Combine(AppData, ".minecraft");

        private sealed record Source(string Id, string Name, Func<bool> Installed, Func<IEnumerable<ForeignInstance>> Scan);

        private IEnumerable<Source> getSources()
        {
            string[] modrinthRoots = [Path.Combine(AppData, "ModrinthApp"), Path.Combine(AppData, "com.modrinth.theseus")];
            yield return new("axoclient-old", "AxoClient (alt)", () => OldAxoRoots.Any(Directory.Exists), oldAxoClient);
            yield return new("modrinth", "Modrinth App", () => modrinthRoots.Any(Directory.Exists), modrinth);
            yield return new("curseforge", "CurseForge", () => CurseForgeRoots.Any(Directory.Exists), curseForge);
            yield return new("prism", "Prism Launcher", () => Directory.Exists(Path.Combine(AppData, "PrismLauncher")),
                () => multiMcStyle("prism", Path.Combine(AppData, "PrismLauncher")));
            yield return new("official", "Minecraft Launcher", () => File.Exists(Path.Combine(DotMinecraft, "launcher_profiles.json")), official);
            yield return new("lunar", "Lunar Client", () => Directory.Exists(Path.Combine(Home, ".lunarclient")), lunar);
            yield return new("norisk", "NoRiskClient", () => Directory.Exists(Path.Combine(AppData, "norisk", "NoRiskClientV3")), noRisk);
            yield return new("badlion", "Badlion Client", () => Directory.Exists(Path.Combine(AppData, "Badlion Client")),
                () => dotMinecraftClient("badlion", "Badlion Client", Path.Combine(AppData, "Badlion Client")));
            yield return new("feather", "Feather Client", () => Directory.Exists(Path.Combine(AppData, ".feather")), feather);
            yield return new("labymod", "LabyMod",
                () => Directory.Exists(Path.Combine(AppData, "LabyMod")) || Directory.Exists(Path.Combine(DotMinecraft, "labymod-neo")), labyMod);
        }

        public ForeignInstance? Get(string id) => _known.GetValueOrDefault(id);

        public async Task<IReadOnlyList<ForeignLauncherView>> Scan(JobContext job)
        {
            var sources = getSources().ToList();
            var done = 0;
            job.Status("import.scanning", ("count", sources.Count));
            var launchers = await Task.WhenAll(sources.Select(source => Task.Run(() =>
            {
                var result = read(source);
                job.Report((double)Interlocked.Increment(ref done) / sources.Count);
                return result;
            }, job.Token)));
            foreach (var instance in launchers.SelectMany(l => l.Instances))
            {
                _known[instance.Id] = instance;
            }

            return launchers
                .OrderByDescending(l => l.Instances.Count > 0)
                .ThenByDescending(l => l.Installed)
                .Select(l => new ForeignLauncherView(l.Source.Id, l.Source.Name, l.Installed,
                    l.Instances.Select(i => i.ToView()).ToList(), l.Problem))
                .ToList();
        }

        /// <summary>Ordner (z. B. ein entpackter Export eines anderen Launchers) als Instanz lesen.</summary>
        public ForeignInstance FromFolder(string root, string fallbackName)
        {
            var instance = readFolder(root, fallbackName);
            complete(instance);
            _known[instance.Id] = instance;
            return instance;
        }

        private sealed record Scanned(Source Source, bool Installed, List<ForeignInstance> Instances, TextRef? Problem);

        private Scanned read(Source source)
        {
            try
            {
                if (!source.Installed())
                {
                    return new Scanned(source, false, [], null);
                }

                var instances = source.Scan().ToList();
                foreach (var instance in instances)
                {
                    complete(instance);
                }

                return new Scanned(source, true, instances.OrderBy(i => i.Problem != null).ThenBy(i => i.Name).ToList(), null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                _appLog.Warn($"Instanzen von {source.Name} konnten nicht gelesen werden", ex);
                return new Scanned(source, true, [], new TextRef("import.problem.readFailed"));
            }
        }

        private void complete(ForeignInstance instance)
        {
            if (instance.Problem == null && !Directory.Exists(instance.GameDir))
            {
                instance.Problem = new TextRef("import.problem.noGameDir");
            }

            instance.Worlds = _worlds.Count(Path.Combine(instance.GameDir, "saves"));
            var mods = instance.ModsDir;
            instance.Mods = (mods != null && Directory.Exists(mods) ? Directory.EnumerateFiles(mods).Count(IsModFile) : 0)
                            + instance.ModrinthMods.Count;
            if (instance.MinecraftVersion == null || instance.Loader == null)
            {
                fromLog(instance);
            }
        }

        // ---------- Modrinth App ----------

        private IEnumerable<ForeignInstance> modrinth()
        {
            foreach (var root in new[] { Path.Combine(AppData, "ModrinthApp"), Path.Combine(AppData, "com.modrinth.theseus") })
            {
                var profiles = Path.Combine(root, "profiles");
                if (!Directory.Exists(profiles))
                {
                    continue;
                }

                var rows = new Dictionary<string, string?[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in _sqliteReader.ReadCopy(Path.Combine(root, "app.db"),
                         [
                             "SELECT path, name, game_version, mod_loader, mod_loader_version FROM profiles",
                             "SELECT path, name, game_version, mod_loader, NULL FROM profiles"
                         ])[0])
                {
                    if (!string.IsNullOrEmpty(r[0]))
                    {
                        rows[r[0]!] = r;
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(profiles))
                {
                    var folder = Path.GetFileName(dir);
                    if (rows.TryGetValue(folder, out var row))
                    {
                        yield return new ForeignInstance
                        {
                            Launcher = "modrinth",
                            Name = row[1] ?? folder,
                            GameDir = dir,
                            Loader = parseLoader(row[3]),
                            MinecraftVersion = row[2],
                            LoaderVersion = row[4]
                        };
                    }
                    else if (readJson(Path.Combine(dir, "profile.json")) is { } json && json.TryGetProperty("metadata", out var meta))
                    {
                        var instance = new ForeignInstance
                        {
                            Launcher = "modrinth",
                            Name = str(meta, "name") ?? folder,
                            GameDir = dir,
                            Loader = parseLoader(str(meta, "loader")),
                            MinecraftVersion = str(meta, "game_version")
                        };
                        if (meta.TryGetProperty("loader_version", out var lv) && lv.ValueKind == JsonValueKind.Object)
                        {
                            instance.LoaderVersion = str(lv, "id");
                        }

                        yield return instance;
                    }
                    else
                    {
                        yield return new ForeignInstance { Launcher = "modrinth", Name = folder, GameDir = dir };
                    }
                }
            }
        }

        // ---------- CurseForge ----------

        private string[] CurseForgeRoots =>
        [
            Path.Combine(Home, "curseforge", "minecraft", "Instances"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Curse", "Minecraft", "Instances")
        ];

        private IEnumerable<ForeignInstance> curseForge()
        {
            foreach (var root in CurseForgeRoots.Where(Directory.Exists))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var json = readJson(Path.Combine(dir, "minecraftinstance.json"));
                    var instance = new ForeignInstance
                    {
                        Launcher = "curseforge",
                        Name = json is { } j ? str(j, "name") ?? Path.GetFileName(dir) : Path.GetFileName(dir),
                        GameDir = dir
                    };
                    if (json is { } data)
                    {
                        instance.MinecraftVersion = str(data, "gameVersion");
                        if (data.TryGetProperty("baseModLoader", out var loader) && loader.ValueKind == JsonValueKind.Object)
                        {
                            var name = str(loader, "name") ?? "";
                            var dash = name.IndexOf('-');
                            instance.Loader = parseLoader(dash > 0 ? name[..dash] : name);
                            instance.LoaderVersion = str(loader, "forgeVersion") ?? (dash > 0 ? name[(dash + 1)..].Split('-')[0] : null);
                            instance.MinecraftVersion = str(loader, "minecraftVersion") ?? instance.MinecraftVersion;
                        }
                        else if (instance.MinecraftVersion != null)
                        {
                            instance.Loader = LoaderType.Vanilla;
                        }
                    }
                    yield return instance;
                }
            }
        }

        // ---------- Prism / MultiMC ----------

        private IEnumerable<ForeignInstance> multiMcStyle(string launcher, string root)
        {
            var instances = Path.Combine(root, "instances");
            if (!Directory.Exists(instances))
            {
                yield break;
            }

            foreach (var dir in Directory.EnumerateDirectories(instances))
            {
                if (File.Exists(Path.Combine(dir, "instance.cfg")))
                {
                    yield return multiMcInstance(launcher, dir, Path.GetFileName(dir));
                }
            }
        }

        private ForeignInstance multiMcInstance(string launcher, string dir, string fallbackName)
        {
            var cfg = Path.Combine(dir, "instance.cfg");
            var name = File.Exists(cfg)
                ? File.ReadLines(cfg).FirstOrDefault(l => l.StartsWith("name=", StringComparison.Ordinal))?[5..].Trim()
                : null;
            var instance = new ForeignInstance
            {
                Launcher = launcher,
                Name = string.IsNullOrEmpty(name) ? fallbackName : name,
                GameDir = new[] { ".minecraft", "minecraft" }.Select(d => Path.Combine(dir, d)).FirstOrDefault(Directory.Exists)
                          ?? Path.Combine(dir, ".minecraft")
            };
            if (readJson(Path.Combine(dir, "mmc-pack.json")) is { } pack && pack.TryGetProperty("components", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                instance.Loader = LoaderType.Vanilla;
                foreach (var part in parts.EnumerateArray())
                {
                    var version = str(part, "version");
                    switch (str(part, "uid"))
                    {
                        case "net.minecraft":
                            instance.MinecraftVersion = version;
                            break;
                        case "net.fabricmc.fabric-loader":
                            (instance.Loader, instance.LoaderVersion) = (LoaderType.Fabric, version);
                            break;
                        case "net.minecraftforge":
                            (instance.Loader, instance.LoaderVersion) = (LoaderType.Forge, version);
                            break;
                        case "net.neoforged":
                            (instance.Loader, instance.LoaderVersion) = (LoaderType.NeoForge, version);
                            break;
                        case "org.quiltmc.quilt-loader":
                            (instance.Loader, instance.LoaderVersion) = (LoaderType.Quilt, version);
                            break;
                    }
                }
            }
            return instance;
        }

        // ---------- Ordner / entpackte ZIP-Exporte ----------

        private ForeignInstance readFolder(string root, string fallbackName)
        {
            if (File.Exists(Path.Combine(root, "instance.cfg")) || File.Exists(Path.Combine(root, "mmc-pack.json")))
            {
                return multiMcInstance("file", root, fallbackName);
            }

            if (readJson(Path.Combine(root, "manifest.json")) is { } manifest
                && manifest.TryGetProperty("minecraft", out var minecraft) && minecraft.ValueKind == JsonValueKind.Object)
            {
                var instance = new ForeignInstance
                {
                    Launcher = "file",
                    Name = str(manifest, "name") ?? fallbackName,
                    GameDir = _pathGuard.SafeCombine(root, str(manifest, "overrides") ?? "overrides") ?? root,
                    MinecraftVersion = str(minecraft, "version"),
                    Hint = new TextRef("import.hint.curseforgePack")
                };
                if (minecraft.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array
                    && loaders.EnumerateArray().Select(l => str(l, "id")).FirstOrDefault(id => id != null) is { } loaderId)
                {
                    var dash = loaderId.IndexOf('-');
                    instance.Loader = parseLoader(dash > 0 ? loaderId[..dash] : loaderId);
                    instance.LoaderVersion = dash > 0 ? loaderId[(dash + 1)..] : null;
                }
                return instance;
            }
            var gameDir = new[] { ".minecraft", "minecraft" }.Select(d => Path.Combine(root, d)).FirstOrDefault(Directory.Exists) ?? root;
            return new ForeignInstance { Launcher = "file", Name = fallbackName, GameDir = gameDir };
        }

        // ---------- Offizieller Launcher ----------

        private IEnumerable<ForeignInstance> official()
        {
            if (readJson(Path.Combine(DotMinecraft, "launcher_profiles.json")) is not { } json
                || !json.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                var p = profile.Value;
                if (p.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = str(p, "type");
                var name = str(p, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = type switch
                    {
                        "latest-release" => "Neueste Version",
                        "latest-snapshot" => "Neuester Snapshot",
                        _ => profile.Name
                    };
                }

                var gameDir = str(p, "gameDir");
                var instance = new ForeignInstance
                {
                    Launcher = "official",
                    Name = name,
                    GameDir = string.IsNullOrWhiteSpace(gameDir) ? DotMinecraft : gameDir,
                    Shared = string.IsNullOrWhiteSpace(gameDir)
                };
                parseVersionId(instance, str(p, "lastVersionId"));
                yield return instance;
            }
        }

        private void parseVersionId(ForeignInstance instance, string? id)
        {
            if (string.IsNullOrEmpty(id) || id.StartsWith("latest-", StringComparison.Ordinal))
            {
                return;
            }

            Match m;
            if ((m = fabricId().Match(id)).Success)
            {
                (instance.Loader, instance.LoaderVersion, instance.MinecraftVersion) = (LoaderType.Fabric, m.Groups[1].Value, m.Groups[2].Value);
            }
            else if ((m = quiltId().Match(id)).Success)
            {
                (instance.Loader, instance.LoaderVersion, instance.MinecraftVersion) = (LoaderType.Quilt, m.Groups[1].Value, m.Groups[2].Value);
            }
            else if ((m = forgeId().Match(id)).Success)
            {
                (instance.Loader, instance.MinecraftVersion, instance.LoaderVersion) = (LoaderType.Forge, m.Groups[1].Value, m.Groups[2].Value);
            }
            else if ((m = neoForgeId().Match(id)).Success)
            {
                (instance.Loader, instance.LoaderVersion) = (LoaderType.NeoForge, m.Groups[1].Value);
            }
            else if ((m = optiFineId().Match(id)).Success)
            {
                (instance.Loader, instance.MinecraftVersion) = (LoaderType.Vanilla, m.Groups[1].Value);
            }
            else if (plainVersionId().IsMatch(id))
            {
                (instance.Loader, instance.MinecraftVersion) = (LoaderType.Vanilla, id);
            }
        }

        // ---------- Alter AxoClient (WPF-Launcher) ----------

        private string[] OldAxoRoots => [Path.Combine(AppData, ".axoclient"), Path.Combine(AppData, ".mclauncher")];

        /// <summary>
        /// Zwei Formate: früher alle Instanzen in <c>settings.json</c> (bzw. <c>launcher-settings.json</c>) unter
        /// "Installations", später je Instanz <c>instances/*/instance.json</c> mit Spielordner <c>.minecraft</c>.
        /// </summary>
        private IEnumerable<ForeignInstance> oldAxoClient()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in OldAxoRoots.Where(Directory.Exists))
            {
                var found = new List<ForeignInstance>();
                var instancesDir = Path.Combine(root, "instances");
                if (Directory.Exists(instancesDir))
                {
                    foreach (var dir in Directory.EnumerateDirectories(instancesDir))
                    {
                        if (readJson(Path.Combine(dir, "instance.json")) is { } json)
                        {
                            found.Add(oldAxoInstance(root, str(json, "name") ?? Path.GetFileName(dir),
                                str(json, "customGameDir") is { Length: > 0 } custom ? custom : Path.Combine(dir, ".minecraft"),
                                str(json, "loader"), str(json, "minecraftVersion"), str(json, "loaderVersion")));
                        }
                    }
                }

                // Ältere Fassung; launcher-settings.json bleibt nach der Umstellung veraltet liegen, darum nur ohne Neueres
                if (found.Count == 0
                    && (readJson(Path.Combine(root, "settings.json")) ?? readJson(Path.Combine(root, "launcher-settings.json"))) is { } settings
                    && settings.TryGetProperty("Installations", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (str(item, "GameDir") is { Length: > 0 } gameDir)
                        {
                            found.Add(oldAxoInstance(root, str(item, "Name") ?? Path.GetFileName(gameDir.TrimEnd('\\', '/')), gameDir,
                                str(item, "Loader"), str(item, "MinecraftVersion"), str(item, "LoaderVersion")));
                        }
                    }
                }

                foreach (var instance in found)
                {
                    if (seen.Add(fullPath(instance.GameDir)))
                    {
                        yield return instance;
                    }
                }
            }
        }

        private ForeignInstance oldAxoInstance(string root, string name, string gameDir, string? loader, string? version,
            string? loaderVersion) =>
            new()
            {
                Launcher = "axoclient-old",
                Name = name,
                GameDir = gameDir,
                // Die erste Instanz nutzte den Launcher-Ordner selbst als Spielordner: nur Spielerdaten übernehmen
                Shared = string.Equals(fullPath(gameDir), fullPath(root), StringComparison.OrdinalIgnoreCase),
                Loader = parseLoader(loader),
                MinecraftVersion = version,
                LoaderVersion = loaderVersion
            };

        private string fullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path;
            }
        }

        // ---------- Lunar Client ----------

        private IEnumerable<ForeignInstance> lunar()
        {
            var root = Path.Combine(Home, ".lunarclient");
            var rows = _sqliteReader.ReadCopy(Path.Combine(root, "db", "profiles.db"),
                ["SELECT name, path, game_version, loaders, game_directory, mods_directory, is_badlion FROM profiles"])[0];
            if (rows.Count == 0)
            {
                foreach (var instance in dotMinecraftClient("lunar", "Lunar Client", root))
                {
                    yield return instance;
                }

                yield break;
            }
            foreach (var row in rows)
            {
                if (lunarProfile(root, row) is { } instance)
                {
                    yield return instance;
                }
            }
        }

        private ForeignInstance? lunarProfile(string root, string?[] row)
        {
            var (name, path, version, loaders) = (row[0], row[1], row[2], row[3]);
            var profilesDir = Path.Combine(root, "profiles");
            if (string.IsNullOrEmpty(path) || _pathGuard.ResolveInside(profilesDir, path) is not { } profileDir)
            {
                return null;
            }

            var gameDir = !string.IsNullOrWhiteSpace(row[4]) && Directory.Exists(row[4]) ? row[4]! : profileDir;
            var shared = !string.Equals(Path.GetFullPath(gameDir).TrimEnd('\\'), Path.GetFullPath(profileDir).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

            LoaderType loader = LoaderType.Vanilla;
            foreach (var candidate in jsonStrings(loaders))
            {
                if (parseLoader(candidate) is { } parsed and not LoaderType.Vanilla)
                {
                    loader = parsed;
                }
            }

            var modsBase = !string.IsNullOrWhiteSpace(row[5]) ? row[5]! : Path.Combine(profileDir, "mods");
            var mods = new[] { Path.Combine(modsBase, $"{loader.ToString().ToLowerInvariant()}-{version}"), modsBase }
                .FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d).Any(IsModFile));
            var config = Path.Combine(profileDir, "config");
            var client = row[6] == "1" ? "Badlion Client" : "Lunar Client";
            return new ForeignInstance
            {
                Launcher = "lunar",
                Name = string.IsNullOrWhiteSpace(name) ? path : name!,
                GameDir = gameDir,
                Shared = shared,
                GameDirMods = false,
                ExtraModsDir = mods,
                ExtraConfigDir = Directory.Exists(config) ? config : null,
                ExtraDataDir = shared ? profileDir : null,
                Loader = loader,
                MinecraftVersion = string.IsNullOrWhiteSpace(version) ? null : version,
                Hint = new TextRef(mods != null ? "import.hint.clientWithMods" : "import.hint.clientBuiltIn", ("launcher", client))
            };
        }

        // ---------- NoRiskClient ----------

        private sealed record NoRiskProfile(string Id, string? Name, string Path, string? Version, string? Loader,
            string? LoaderVersion, string? Group, bool SharedFolder, bool Standard, List<ForeignModrinthMod> Mods);

        private IEnumerable<ForeignInstance> noRisk()
        {
            var root = Path.Combine(AppData, "norisk", "NoRiskClientV3");
            var profilesDir = Path.Combine(root, "data", "profiles");
            var tables = _sqliteReader.ReadCopy(Path.Combine(root, "meta", "app.db"),
                ["SELECT id, name, path, game_version, loader, loader_version, group_name, use_shared_minecraft_folder, " +
                 "is_standard_version FROM profiles"],
                ["SELECT profile_id, project_id, version_id, enabled FROM profile_mods " +
                 "WHERE source_type = 'modrinth' AND project_id IS NOT NULL"]);
            var profiles = new List<NoRiskProfile>();
            foreach (var r in tables[0])
            {
                if (string.IsNullOrEmpty(r[0]) || string.IsNullOrEmpty(r[2]))
                {
                    continue;
                }

                var mods = tables[1].Where(m => m[0] == r[0] && !string.IsNullOrEmpty(m[1]))
                    .Select(m => new ForeignModrinthMod(m[1]!, m[2], m[3] != "0")).ToList();
                profiles.Add(new NoRiskProfile(r[0]!, r[1], r[2]!, r[3], r[4], r[5], r[6], r[7] == "1", r[8] == "1", mods));
            }
            if (profiles.Count == 0)
            {
                profiles.AddRange(noRiskLegacyProfiles(Path.Combine(root, "profiles.json")));
            }

            foreach (var profile in profiles)
            {
                if (noRiskInstance(profilesDir, profile) is { } instance)
                {
                    yield return instance;
                }
            }
        }

        private IEnumerable<NoRiskProfile> noRiskLegacyProfiles(string file)
        {
            if (readJson(file) is not { ValueKind: JsonValueKind.Array } list)
            {
                yield break;
            }

            foreach (var p in list.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object || str(p, "id") is not { } id || str(p, "path") is not { } path)
                {
                    continue;
                }

                var mods = new List<ForeignModrinthMod>();
                if (p.TryGetProperty("mods", out var modList) && modList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mod in modList.EnumerateArray())
                    {
                        if (mod.ValueKind == JsonValueKind.Object && mod.TryGetProperty("source", out var source)
                            && source.ValueKind == JsonValueKind.Object && str(source, "type") == "modrinth"
                            && str(source, "project_id") is { } project)
                        {
                            mods.Add(new ForeignModrinthMod(project, str(source, "version_id"),
                                !(mod.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)));
                        }
                    }
                }

                yield return new NoRiskProfile(id, str(p, "name"), path, str(p, "game_version"), str(p, "loader"), str(p, "loader_version"),
                    str(p, "group"),
                    p.TryGetProperty("use_shared_minecraft_folder", out var sharedFolder) && sharedFolder.ValueKind == JsonValueKind.True,
                    p.TryGetProperty("is_standard_version", out var standard) && standard.ValueKind == JsonValueKind.True,
                    mods);
            }
        }

        private ForeignInstance? noRiskInstance(string profilesDir, NoRiskProfile profile)
        {
            var group = profile.Group?.Trim();
            var isolated = group != null && group.ToLowerInvariant() is "server" or "modpacks";
            string? dir;
            if (profile.SharedFolder && !isolated && !string.IsNullOrEmpty(group))
            {
                dir = group.ToLowerInvariant() is "nrc" or "noriskclient" or "norisk client"
                    ? Path.Combine(profilesDir, "noriskclient", isLegacyMinecraft(profile.Version) ? "legacy" : "new")
                    : _pathGuard.ResolveInside(Path.Combine(profilesDir, "groups"),
                        string.Concat(group.ToLowerInvariant().Where(c => !Path.GetInvalidFileNameChars().Contains(c))));
            }
            else
            {
                dir = _pathGuard.ResolveInside(profilesDir, profile.Path);
            }

            if (profile.Standard && (dir == null || !Directory.Exists(dir)))
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Path : profile.Name!;
            if (dir == null)
            {
                return new ForeignInstance { Launcher = "norisk", Name = name, GameDir = profilesDir, Problem = new TextRef("import.problem.invalidPath") };
            }

            var loader = parseLoader(profile.Loader);
            var loaderName = (loader ?? LoaderType.Vanilla).ToString().ToLowerInvariant();
            var idText = profile.Id.Replace("-", "");
            var shortId = idText.Length >= 4 ? idText[..2] + idText[^2..] : idText;
            var modsDir = Path.Combine(dir, "mods");
            var mods = new[]
                {
                    Path.Combine(modsDir, $"nrc-{profile.Version}-{loaderName}-{shortId}"),
                    Path.Combine(modsDir, $"nrc-{profile.Version}-{loaderName}"),
                    modsDir
                }
                .FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d).Any(IsModFile));
            return new ForeignInstance
            {
                Launcher = "norisk",
                Name = name,
                GameDir = dir,
                GameDirMods = false,
                ExtraModsDir = mods,
                ModrinthMods = profile.Mods,
                Loader = loader,
                MinecraftVersion = profile.Version,
                LoaderVersion = profile.LoaderVersion,
                Hint = new TextRef(mods != null ? "import.hint.noriskWithFiles" : "import.hint.norisk")
            };
        }

        private bool isLegacyMinecraft(string? version)
        {
            var parts = (version ?? "").Split('.');
            return parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var minor) && minor < 13;
        }

        // ---------- Clients auf .minecraft (Badlion, Feather, LabyMod) ----------

        private IEnumerable<ForeignInstance> dotMinecraftClient(string id, string launcher, string marker)
        {
            if (!Directory.Exists(marker) || !Directory.Exists(DotMinecraft))
            {
                yield break;
            }

            yield return new ForeignInstance
            {
                Launcher = id,
                Name = launcher,
                GameDir = DotMinecraft,
                Shared = true,
                GameDirMods = false,
                Hint = new TextRef("import.hint.clientShared", ("launcher", launcher))
            };
        }

        private IEnumerable<ForeignInstance> feather()
        {
            var root = Path.Combine(AppData, ".feather");
            if (!Directory.Exists(DotMinecraft))
            {
                yield break;
            }

            var userMods = Path.Combine(root, "user-mods");
            var any = false;
            if (Directory.Exists(userMods))
            {
                foreach (var dir in Directory.EnumerateDirectories(userMods))
                {
                    var m = featherFolder().Match(Path.GetFileName(dir));
                    if (!m.Success || !Directory.EnumerateFiles(dir, "*.jar").Any())
                    {
                        continue;
                    }

                    any = true;
                    yield return new ForeignInstance
                    {
                        Launcher = "feather",
                        Name = $"Feather {m.Groups[1].Value}",
                        GameDir = DotMinecraft,
                        Shared = true,
                        GameDirMods = false,
                        ExtraModsDir = dir,
                        Loader = LoaderType.Fabric,
                        MinecraftVersion = m.Groups[1].Value,
                        Hint = new TextRef("import.hint.clientWithMods", ("launcher", "Feather"))
                    };
                }
            }

            if (!any)
            {
                foreach (var instance in dotMinecraftClient("feather", "Feather Client", root))
                {
                    yield return instance;
                }
            }
        }

        private IEnumerable<ForeignInstance> labyMod()
        {
            var launcherRoot = Path.Combine(AppData, "LabyMod");
            if (readJson(Path.Combine(launcherRoot, "instances.json")) is { } json
                && json.TryGetProperty("instances", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    if (labyModInstance(launcherRoot, entry) is { } instance)
                    {
                        yield return instance;
                    }
                }

                yield break;
            }
            var root = Path.Combine(DotMinecraft, "labymod-neo");
            var fabric = Path.Combine(root, "fabric");
            var any = false;
            if (Directory.Exists(fabric))
            {
                foreach (var dir in Directory.EnumerateDirectories(fabric))
                {
                    var mods = Path.Combine(dir, "mods");
                    if (!Directory.Exists(mods) || !Directory.EnumerateFiles(mods, "*.jar").Any())
                    {
                        continue;
                    }

                    any = true;
                    var version = Path.GetFileName(dir);
                    yield return new ForeignInstance
                    {
                        Launcher = "labymod",
                        Name = $"LabyMod {version}",
                        GameDir = DotMinecraft,
                        Shared = true,
                        GameDirMods = false,
                        ExtraModsDir = mods,
                        Loader = LoaderType.Fabric,
                        MinecraftVersion = version,
                        Hint = new TextRef("import.hint.clientWithMods", ("launcher", "LabyMod"))
                    };
                }
            }

            if (!any)
            {
                foreach (var instance in dotMinecraftClient("labymod", "LabyMod", root))
                {
                    yield return instance;
                }
            }
        }

        private ForeignInstance? labyModInstance(string launcherRoot, JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object || str(entry, "uid") is not { } uid || !_pathGuard.IsSafeFileName(uid))
            {
                return null;
            }

            string? version = null;
            if (entry.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                version = components.EnumerateArray()
                    .Where(c => c.ValueKind == JsonValueKind.Object && str(c, "uid") == "net.minecraft")
                    .Select(c => str(c, "version"))
                    .FirstOrDefault();
            }

            var instanceDir = Path.Combine(launcherRoot, "instances", uid);
            var separate = entry.TryGetProperty("separateGameDirectory", out var sep) && sep.ValueKind == JsonValueKind.True;
            var gameDir = separate
                ? new[] { "gameDirectory", "game", "minecraft", ".minecraft" }.Select(d => Path.Combine(instanceDir, d)).FirstOrDefault(Directory.Exists)
                : null;
            var candidates = new List<string>();
            if (version != null)
            {
                candidates.Add(Path.Combine(instanceDir, "loader", "fabric", version, "mods"));
            }

            candidates.Add(Path.Combine(DotMinecraft, "labymod-neo", "instances", uid, "mods"));
            if (version != null)
            {
                candidates.Add(Path.Combine(DotMinecraft, "labymod-neo", "fabric", version, "mods"));
            }

            var mods = candidates.FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d, "*.jar").Any());
            return new ForeignInstance
            {
                Launcher = "labymod",
                Name = str(entry, "name") ?? $"LabyMod {version}",
                GameDir = gameDir ?? DotMinecraft,
                Shared = gameDir == null,
                GameDirMods = false,
                ExtraModsDir = mods,
                ExtraConfigDir = mods != null ? Path.Combine(Path.GetDirectoryName(mods)!, "config") : null,
                Loader = mods != null ? LoaderType.Fabric : LoaderType.Vanilla,
                MinecraftVersion = version,
                Hint = new TextRef(mods != null ? "import.hint.clientWithMods" : "import.hint.clientBuiltIn", ("launcher", "LabyMod"))
            };
        }

        // ---------- Hilfen ----------

        /// <summary>Fehlen Version oder Loader, verrät sie oft der Anfang von <c>logs/latest.log</c>.</summary>
        private void fromLog(ForeignInstance instance)
        {
            var log = Path.Combine(instance.GameDir, "logs", "latest.log");
            if (!File.Exists(log))
            {
                return;
            }

            try
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var head = new StringBuilder();
                for (var i = 0; i < 300 && reader.ReadLine() is { } line; i++)
                {
                    head.AppendLine(line);
                }

                var text = head.ToString();
                Match m;
                if ((m = fabricLog().Match(text)).Success)
                {
                    apply(instance, LoaderType.Fabric, m.Groups[1].Value, m.Groups[2].Value);
                }
                else if ((m = quiltLog().Match(text)).Success)
                {
                    apply(instance, LoaderType.Quilt, m.Groups[1].Value, m.Groups[2].Value);
                }
                else if ((m = neoForgeLog().Match(text)).Success)
                {
                    apply(instance, LoaderType.NeoForge, m.Groups[2].Value, m.Groups[1].Value);
                }
                else if ((m = forgeLog().Match(text)).Success)
                {
                    apply(instance, LoaderType.Forge, m.Groups[1].Value, m.Groups[2].Value);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private void apply(ForeignInstance instance, LoaderType loader, string version, string loaderVersion)
        {
            if (instance.Loader == null)
            {
                instance.Loader = loader;
                instance.LoaderVersion ??= loaderVersion;
            }
            instance.MinecraftVersion ??= version;
        }

        private LoaderType? parseLoader(string? name) => name?.Trim().ToLowerInvariant() switch
        {
            "vanilla" or "" or "none" => LoaderType.Vanilla,
            "fabric" => LoaderType.Fabric,
            "forge" => LoaderType.Forge,
            "quilt" => LoaderType.Quilt,
            "neoforge" or "neo_forge" or "neoforged" => LoaderType.NeoForge,
            _ => null
        };

        private JsonElement? readJson(string file)
        {
            if (!File.Exists(file))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                return doc.RootElement.Clone();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        private string? str(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private IEnumerable<string> jsonStrings(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                    : [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        [GeneratedRegex(@"^fabric-loader-([^-]+)-(.+)$")]
        private partial Regex fabricId();

        [GeneratedRegex(@"^quilt-loader-([^-]+)-(.+)$")]
        private partial Regex quiltId();

        [GeneratedRegex(@"^(.+?)-forge-?(.+)$", RegexOptions.IgnoreCase)]
        private partial Regex forgeId();

        [GeneratedRegex(@"^neoforge-(.+)$", RegexOptions.IgnoreCase)]
        private partial Regex neoForgeId();

        [GeneratedRegex(@"^(.+?)-OptiFine", RegexOptions.IgnoreCase)]
        private partial Regex optiFineId();

        [GeneratedRegex(@"^\d+\.\d+(\.\d+)?$|^\d\dw\d\d[a-z]$")]
        private partial Regex plainVersionId();

        [GeneratedRegex(@"^(.+)-fabric$", RegexOptions.IgnoreCase)]
        private partial Regex featherFolder();

        [GeneratedRegex(@"Loading Minecraft (\S+) with Fabric Loader (\S+)")]
        private partial Regex fabricLog();

        [GeneratedRegex(@"Loading Minecraft (\S+) with Quilt Loader (\S+)")]
        private partial Regex quiltLog();

        [GeneratedRegex(@"--fml\.neoForgeVersion, ([^,\]\s]+).*?--fml\.mcVersion, ([^,\]\s]+)")]
        private partial Regex neoForgeLog();

        [GeneratedRegex(@"--fml\.mcVersion, ([^,\]\s]+).*?--fml\.forgeVersion, ([^,\]\s]+)")]
        private partial Regex forgeLog();
    }
}

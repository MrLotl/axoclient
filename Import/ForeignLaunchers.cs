using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AxoClient.Import;

public enum ForeignLoader
{
    Unknown,
    Vanilla,
    Fabric,
    Forge,
    Quilt,
    NeoForge
}

public sealed class ForeignInstance
{
    public required string Launcher { get; init; }
    public required string Name { get; init; }
    public required string GameDir { get; init; }

    public bool Shared { get; init; }
    public bool GameDirMods { get; init; } = true;
    public string? ExtraModsDir { get; init; }
    public string? ExtraConfigDir { get; init; }
    public string? ExtraDataDir { get; init; }
    public List<ForeignModrinthMod> ModrinthMods { get; init; } = [];

    public ForeignLoader Loader { get; set; }
    public string? MinecraftVersion { get; set; }
    public string? LoaderVersion { get; set; }

    public int Worlds { get; set; }
    public int Mods { get; set; }
    public string? Hint { get; set; }
    public string? Problem { get; set; }

    public bool Importable => Problem == null;
    public bool Complete => MinecraftVersion != null && AsLoaderType(Loader) != null;

    public string? ModsDir => ExtraModsDir ?? (GameDirMods ? Path.Combine(GameDir, "mods") : null);

    public static bool IsModFile(string file) =>
        file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

    public static LoaderType? AsLoaderType(ForeignLoader loader) => loader switch
    {
        ForeignLoader.Vanilla => LoaderType.Vanilla,
        ForeignLoader.Fabric => LoaderType.Fabric,
        ForeignLoader.Forge => LoaderType.Forge,
        _ => null
    };

    public string Description
    {
        get
        {
            var parts = new List<string>
            {
                Loader == ForeignLoader.Unknown ? "Loader unbekannt" : Loader.ToString(),
                MinecraftVersion ?? "Version unbekannt"
            };
            if (Mods > 0)
                parts.Add(Mods == 1 ? "1 Mod" : $"{Mods} Mods");
            if (Worlds > 0)
                parts.Add(Worlds == 1 ? "1 Welt" : $"{Worlds} Welten");
            return string.Join(" · ", parts);
        }
    }
}

public sealed record ForeignModrinthMod(string ProjectId, string? VersionId, bool Enabled);

public sealed record ForeignLauncher(string Name, bool Installed, List<ForeignInstance> Instances, string? Problem = null);

public static class ForeignLaunchers
{
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string DotMinecraft => AppPaths.OfficialMinecraftDir;

    private sealed record Source(string Name, Func<bool> Installed, Func<IEnumerable<ForeignInstance>> Scan);

    private static IEnumerable<Source> Sources()
    {
        string[] modrinthRoots = [Path.Combine(AppData, "ModrinthApp"), Path.Combine(AppData, "com.modrinth.theseus")];
        yield return new("Modrinth App", () => modrinthRoots.Any(Directory.Exists), Modrinth);
        yield return new("CurseForge", () => CurseForgeRoots.Any(Directory.Exists), CurseForge);
        yield return new("Prism Launcher", () => Directory.Exists(Path.Combine(AppData, "PrismLauncher")),
            () => MultiMcStyle("Prism Launcher", Path.Combine(AppData, "PrismLauncher")));
        yield return new("Offizieller Launcher", () => File.Exists(Path.Combine(DotMinecraft, "launcher_profiles.json")),
            Official);
        yield return new("Lunar Client", () => Directory.Exists(Path.Combine(Home, ".lunarclient")), Lunar);
        yield return new("NoRiskClient", () => Directory.Exists(Path.Combine(AppData, "norisk", "NoRiskClientV3")), NoRisk);
        yield return new("Badlion Client", () => Directory.Exists(Path.Combine(AppData, "Badlion Client")),
            () => DotMinecraftClient("Badlion Client", Path.Combine(AppData, "Badlion Client")));
        yield return new("Feather Client", () => Directory.Exists(Path.Combine(AppData, ".feather")), Feather);
        yield return new("LabyMod", () => Directory.Exists(Path.Combine(AppData, "LabyMod"))
                                         || Directory.Exists(Path.Combine(DotMinecraft, "labymod-neo")), LabyMod);
    }

    public static async Task<List<ForeignLauncher>> ScanAsync(WorkProgress progress)
    {
        var sources = Sources().ToList();
        var done = 0;
        var (status, cancel) = (progress.Text, progress.Cancel);
        status.Report($"Durchsuche {sources.Count} Launcher...");
        var tasks = sources.Select(source => Task.Run(() =>
        {
            var launcher = Read(source);
            cancel.ThrowIfCancellationRequested();
            var count = Interlocked.Increment(ref done);
            progress.Fraction.Report((double)count / sources.Count);
            status.Report(launcher.Installed
                ? $"{launcher.Name}: {launcher.Instances.Count} Instanzen gefunden ({count} von {sources.Count} Launchern)"
                : $"{count} von {sources.Count} Launchern durchsucht...");
            return launcher;
        }, cancel));
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static ForeignLauncher Read(Source source)
    {
        List<ForeignInstance> instances;
        try
        {
            if (!source.Installed())
                return new ForeignLauncher(source.Name, false, []);
            instances = source.Scan().ToList();
            foreach (var instance in instances)
                Complete(instance);
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Instanzen von {source.Name} suchen", ex);
            return new ForeignLauncher(source.Name, true, [],
                "Die Instanzen konnten nicht gelesen werden: " + ErrorReport.Short(ex));
        }
        return new ForeignLauncher(source.Name, true, instances.OrderByDescending(i => i.Importable).ToList());
    }

    private static void Complete(ForeignInstance instance)
    {
        if (instance.Problem == null && !Directory.Exists(instance.GameDir))
            instance.Problem = "Der Spielordner fehlt – vermutlich wurde die Instanz nie gestartet.";
        Count(instance);
        if (instance.MinecraftVersion == null || instance.Loader == ForeignLoader.Unknown)
            FromLog(instance);
        if (instance.Loader is ForeignLoader.Quilt or ForeignLoader.NeoForge)
            instance.Hint ??= $"{instance.Loader} kann AxoClient nicht starten. Welten, Einstellungen und Pakete " +
                              "kommen mit, die Mods nicht.";
    }

    private static IEnumerable<ForeignInstance> Modrinth()
    {
        foreach (var root in new[] { Path.Combine(AppData, "ModrinthApp"), Path.Combine(AppData, "com.modrinth.theseus") })
        {
            var profiles = Path.Combine(root, "profiles");
            if (!Directory.Exists(profiles))
                continue;
            var rows = ModrinthDatabase(Path.Combine(root, "app.db"));
            foreach (var dir in Directory.GetDirectories(profiles))
            {
                var folder = Path.GetFileName(dir);
                var instance = new ForeignInstance { Launcher = "Modrinth App", Name = folder, GameDir = dir };
                if (rows.TryGetValue(folder, out var row))
                {
                    instance = new ForeignInstance { Launcher = "Modrinth App", Name = row.Name ?? folder, GameDir = dir };
                    instance.Loader = ParseLoader(row.Loader);
                    instance.MinecraftVersion = row.Version;
                    instance.LoaderVersion = row.LoaderVersion;
                }
                else if (JsonFiles.ReadObject(Path.Combine(dir, "profile.json"), lenient: true) is { } json
                         && json.TryGetProperty("metadata", out var meta))
                {
                    instance = new ForeignInstance
                    {
                        Launcher = "Modrinth App", Name = JsonFiles.String(meta, "name") ?? folder, GameDir = dir
                    };
                    instance.Loader = ParseLoader(JsonFiles.String(meta, "loader"));
                    instance.MinecraftVersion = JsonFiles.String(meta, "game_version");
                    if (meta.TryGetProperty("loader_version", out var lv) && lv.ValueKind == JsonValueKind.Object)
                        instance.LoaderVersion = JsonFiles.String(lv, "id");
                }
                yield return instance;
            }
        }
    }

    private sealed record ModrinthRow(string? Name, string? Version, string? Loader, string? LoaderVersion);

    private static Dictionary<string, ModrinthRow> ModrinthDatabase(string file)
    {
        var rows = new Dictionary<string, ModrinthRow>(StringComparer.OrdinalIgnoreCase);
        var tables = Sqlite.ReadCopy(file,
        [
            "SELECT path, name, game_version, mod_loader, mod_loader_version FROM profiles",
            "SELECT path, name, game_version, mod_loader, NULL FROM profiles"
        ]);
        foreach (var r in tables[0])
            if (!string.IsNullOrEmpty(r[0]))
                rows[r[0]!] = new ModrinthRow(r[1], r[2], r[3], r[4]);
        return rows;
    }

    private static string[] CurseForgeRoots =>
    [
        Path.Combine(Home, "curseforge", "minecraft", "Instances"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Curse", "Minecraft", "Instances")
    ];

    private static IEnumerable<ForeignInstance> CurseForge()
    {
        foreach (var root in CurseForgeRoots.Where(Directory.Exists))
            foreach (var dir in Directory.GetDirectories(root))
            {
                var json = JsonFiles.ReadObject(Path.Combine(dir, "minecraftinstance.json"), lenient: true);
                var instance = new ForeignInstance
                {
                    Launcher = "CurseForge",
                    Name = json is { } j ? JsonFiles.String(j, "name") ?? Path.GetFileName(dir) : Path.GetFileName(dir),
                    GameDir = dir
                };
                if (json is { } root2)
                {
                    instance.MinecraftVersion = JsonFiles.String(root2, "gameVersion");
                    if (root2.TryGetProperty("baseModLoader", out var loader) && loader.ValueKind == JsonValueKind.Object)
                    {
                        var name = JsonFiles.String(loader, "name") ?? "";
                        var dash = name.IndexOf('-');
                        instance.Loader = ParseLoader(dash > 0 ? name[..dash] : name);
                        instance.LoaderVersion = JsonFiles.String(loader, "forgeVersion") ?? (dash > 0 ? name[(dash + 1)..].Split('-')[0] : null);
                        instance.MinecraftVersion = JsonFiles.String(loader, "minecraftVersion") ?? instance.MinecraftVersion;
                    }
                    else if (instance.MinecraftVersion != null)
                        instance.Loader = ForeignLoader.Vanilla;
                }
                yield return instance;
            }
    }

    private static IEnumerable<ForeignInstance> MultiMcStyle(string launcher, string root)
    {
        var instances = Path.Combine(root, "instances");
        if (!Directory.Exists(instances))
            yield break;
        foreach (var dir in Directory.GetDirectories(instances))
        {
            var cfg = Path.Combine(dir, "instance.cfg");
            if (!File.Exists(cfg))
                continue;
            var gameDir = new[] { ".minecraft", "minecraft" }.Select(d => Path.Combine(dir, d)).FirstOrDefault(Directory.Exists)
                          ?? Path.Combine(dir, ".minecraft");
            var name = File.ReadLines(cfg).FirstOrDefault(l => l.StartsWith("name=", StringComparison.Ordinal))?[5..].Trim();
            var instance = new ForeignInstance
            {
                Launcher = launcher, Name = string.IsNullOrEmpty(name) ? Path.GetFileName(dir) : name, GameDir = gameDir
            };
            if (JsonFiles.ReadObject(Path.Combine(dir, "mmc-pack.json"), lenient: true) is { } pack && pack.TryGetProperty("components", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                instance.Loader = ForeignLoader.Vanilla;
                foreach (var part in parts.EnumerateArray())
                {
                    var version = JsonFiles.String(part, "version");
                    switch (JsonFiles.String(part, "uid"))
                    {
                        case "net.minecraft":
                            instance.MinecraftVersion = version;
                            break;
                        case "net.fabricmc.fabric-loader":
                            (instance.Loader, instance.LoaderVersion) = (ForeignLoader.Fabric, version);
                            break;
                        case "net.minecraftforge":
                            (instance.Loader, instance.LoaderVersion) = (ForeignLoader.Forge, version);
                            break;
                        case "net.neoforged":
                            (instance.Loader, instance.LoaderVersion) = (ForeignLoader.NeoForge, version);
                            break;
                        case "org.quiltmc.quilt-loader":
                            (instance.Loader, instance.LoaderVersion) = (ForeignLoader.Quilt, version);
                            break;
                    }
                }
            }
            yield return instance;
        }
    }

    private static IEnumerable<ForeignInstance> Official()
    {
        if (JsonFiles.ReadObject(Path.Combine(DotMinecraft, "launcher_profiles.json"), lenient: true) is not { } json
            || !json.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (var profile in profiles.EnumerateObject())
        {
            var p = profile.Value;
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            var type = JsonFiles.String(p, "type");
            var name = JsonFiles.String(p, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = type switch
                {
                    "latest-release" => "Neueste Version",
                    "latest-snapshot" => "Neuester Snapshot",
                    _ => profile.Name
                };
            var gameDir = JsonFiles.String(p, "gameDir");
            var instance = new ForeignInstance
            {
                Launcher = "Offizieller Launcher",
                Name = name!,
                GameDir = string.IsNullOrWhiteSpace(gameDir) ? DotMinecraft : gameDir,
                Shared = true
            };
            ParseVersionId(instance, JsonFiles.String(p, "lastVersionId"));
            yield return instance;
        }
    }

    private static void ParseVersionId(ForeignInstance instance, string? id)
    {
        if (string.IsNullOrEmpty(id) || id.StartsWith("latest-", StringComparison.Ordinal))
            return;
        Match m;
        if ((m = Regex.Match(id, @"^fabric-loader-([^-]+)-(.+)$")).Success)
            (instance.Loader, instance.LoaderVersion, instance.MinecraftVersion) = (ForeignLoader.Fabric, m.Groups[1].Value, m.Groups[2].Value);
        else if ((m = Regex.Match(id, @"^quilt-loader-([^-]+)-(.+)$")).Success)
            (instance.Loader, instance.LoaderVersion, instance.MinecraftVersion) = (ForeignLoader.Quilt, m.Groups[1].Value, m.Groups[2].Value);
        else if ((m = Regex.Match(id, @"^(.+?)-forge-?(.+)$", RegexOptions.IgnoreCase)).Success)
            (instance.Loader, instance.MinecraftVersion, instance.LoaderVersion) = (ForeignLoader.Forge, m.Groups[1].Value, m.Groups[2].Value);
        else if ((m = Regex.Match(id, @"^neoforge-(.+)$", RegexOptions.IgnoreCase)).Success)
            (instance.Loader, instance.LoaderVersion) = (ForeignLoader.NeoForge, m.Groups[1].Value);
        else if ((m = Regex.Match(id, @"^(.+?)-OptiFine", RegexOptions.IgnoreCase)).Success)
            (instance.Loader, instance.MinecraftVersion) = (ForeignLoader.Vanilla, m.Groups[1].Value);
        else if (Regex.IsMatch(id, @"^\d+\.\d+(\.\d+)?$|^\d\dw\d\d[a-z]$"))
            (instance.Loader, instance.MinecraftVersion) = (ForeignLoader.Vanilla, id);
    }

    private static IEnumerable<ForeignInstance> Lunar()
    {
        var root = Path.Combine(Home, ".lunarclient");
        if (!Directory.Exists(root))
            yield break;
        var rows = Sqlite.ReadCopy(Path.Combine(root, "db", "profiles.db"),
            ["SELECT name, path, game_version, loaders, game_directory, mods_directory, is_badlion FROM profiles"])[0];
        if (rows.Count == 0)
        {
            foreach (var instance in DotMinecraftClient("Lunar Client", root))
                yield return instance;
            yield break;
        }
        foreach (var row in rows)
            if (LunarProfile(root, row) is { } instance)
                yield return instance;
    }

    private static ForeignInstance? LunarProfile(string root, string?[] row)
    {
        var (name, path, version, loaders) = (row[0], row[1], row[2], row[3]);
        var profilesDir = Path.Combine(root, "profiles");
        if (string.IsNullOrEmpty(path) || FileOps.ResolveInside(profilesDir, path) is not { } profileDir)
            return null;
        var gameDir = !string.IsNullOrWhiteSpace(row[4]) && Directory.Exists(row[4]) ? row[4]! : profileDir;
        var shared = !FileOps.SamePath(gameDir, profileDir);

        var loader = ForeignLoader.Vanilla;
        foreach (var candidate in JsonStrings(loaders))
            if (ParseLoader(candidate) is not (ForeignLoader.Vanilla or ForeignLoader.Unknown) and var parsed)
                loader = parsed;

        var modsBase = !string.IsNullOrWhiteSpace(row[5]) ? row[5]! : Path.Combine(profileDir, "mods");
        var mods = new[] { Path.Combine(modsBase, $"{loader.ToString().ToLowerInvariant()}-{version}"), modsBase }
            .FirstOrDefault(d => Directory.Exists(d) && Directory.GetFiles(d).Any(ForeignInstance.IsModFile));
        var config = Path.Combine(profileDir, "config");

        var launcher = row[6] == "1" ? "Badlion Client" : "Lunar Client";
        return new ForeignInstance
        {
            Launcher = "Lunar Client",
            Name = string.IsNullOrWhiteSpace(name) ? path : name!,
            GameDir = gameDir,
            Shared = shared,
            GameDirMods = false,
            ExtraModsDir = mods,
            ExtraConfigDir = Directory.Exists(config) ? config : null,
            ExtraDataDir = shared ? profileDir : null,
            Loader = loader,
            MinecraftVersion = string.IsNullOrWhiteSpace(version) ? null : version,
            Hint = $"Die eingebauten Funktionen von {launcher} (Minimap, Zoom, deren Einstellungen) lassen sich nicht " +
                   "übertragen" + (mods != null ? ", die hinzugefügten Mods schon." : ".")
        };
    }

    private static IEnumerable<ForeignInstance> NoRisk()
    {
        var root = Path.Combine(AppData, "norisk", "NoRiskClientV3");
        if (!Directory.Exists(root))
            yield break;
        var profilesDir = Path.Combine(root, "data", "profiles");

        var profiles = new List<NoRiskProfile>();
        var tables = Sqlite.ReadCopy(Path.Combine(root, "meta", "app.db"),
            ["SELECT id, name, path, game_version, loader, loader_version, group_name, use_shared_minecraft_folder, " +
             "is_standard_version FROM profiles"],
            ["SELECT profile_id, project_id, version_id, enabled FROM profile_mods " +
             "WHERE source_type = 'modrinth' AND project_id IS NOT NULL"]);
        foreach (var r in tables[0])
        {
            if (string.IsNullOrEmpty(r[0]) || string.IsNullOrEmpty(r[2]))
                continue;
            var mods = tables[1].Where(m => m[0] == r[0] && !string.IsNullOrEmpty(m[1]))
                .Select(m => new ForeignModrinthMod(m[1]!, m[2], m[3] != "0"))
                .ToList();
            profiles.Add(new NoRiskProfile(r[0]!, r[1], r[2]!, r[3], r[4], r[5], r[6], r[7] == "1", r[8] == "1", mods));
        }
        if (profiles.Count == 0)
            profiles.AddRange(NoRiskLegacyProfiles(Path.Combine(root, "profiles.json")));

        foreach (var profile in profiles)
            if (NoRiskInstance(profilesDir, profile) is { } instance)
                yield return instance;
    }

    private sealed record NoRiskProfile(string Id, string? Name, string Path, string? Version, string? Loader,
        string? LoaderVersion, string? Group, bool SharedFolder, bool Standard, List<ForeignModrinthMod> Mods);

    private static IEnumerable<NoRiskProfile> NoRiskLegacyProfiles(string file)
    {
        if (!File.Exists(file))
            yield break;
        JsonElement list;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            list = doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("NoRisk-Profile lesen", ex);
            yield break;
        }
        if (list.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object || JsonFiles.String(p, "id") is not { } id || JsonFiles.String(p, "path") is not { } path)
                continue;
            var mods = new List<ForeignModrinthMod>();
            if (p.TryGetProperty("mods", out var modList) && modList.ValueKind == JsonValueKind.Array)
                foreach (var mod in modList.EnumerateArray())
                    if (mod.ValueKind == JsonValueKind.Object && mod.TryGetProperty("source", out var source)
                        && source.ValueKind == JsonValueKind.Object && JsonFiles.String(source, "type") == "modrinth"
                        && JsonFiles.String(source, "project_id") is { } project)
                        mods.Add(new ForeignModrinthMod(project, JsonFiles.String(source, "version_id"),
                            !(mod.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)));
            yield return new NoRiskProfile(id, JsonFiles.String(p, "name"), path, JsonFiles.String(p, "game_version"), JsonFiles.String(p, "loader"),
                JsonFiles.String(p, "loader_version"), JsonFiles.String(p, "group"),
                p.TryGetProperty("use_shared_minecraft_folder", out var sharedFolder) && sharedFolder.ValueKind == JsonValueKind.True,
                p.TryGetProperty("is_standard_version", out var standard) && standard.ValueKind == JsonValueKind.True,
                mods);
        }
    }

    private static ForeignInstance? NoRiskInstance(string profilesDir, NoRiskProfile profile)
    {
        var group = profile.Group?.Trim();
        var isolated = group != null && group.ToLowerInvariant() is "server" or "modpacks";
        string? dir;
        if (profile.SharedFolder && !isolated && !string.IsNullOrEmpty(group))
        {
            if (group.ToLowerInvariant() is "nrc" or "noriskclient" or "norisk client")
                dir = Path.Combine(profilesDir, "noriskclient", IsLegacyMinecraft(profile.Version) ? "legacy" : "new");
            else
                dir = FileOps.ResolveInside(Path.Combine(profilesDir, "groups"),
                    string.Concat(group.ToLowerInvariant().Where(c => !Path.GetInvalidFileNameChars().Contains(c))));
        }
        else
            dir = FileOps.ResolveInside(profilesDir, profile.Path);
        if (profile.Standard && (dir == null || !Directory.Exists(dir)))
            return null;
        var name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Path : profile.Name!;
        if (dir == null)
            return new ForeignInstance
            {
                Launcher = "NoRiskClient", Name = name, GameDir = profilesDir,
                Problem = "Der Speicherort dieses Profils ist ungültig."
            };

        var loader = ParseLoader(profile.Loader);
        var loaderName = loader.ToString().ToLowerInvariant();
        var idText = profile.Id.Replace("-", "");
        var shortId = idText.Length >= 4 ? idText[..2] + idText[^2..] : idText;
        var modsDir = Path.Combine(dir, "mods");
        var mods = new[]
            {
                Path.Combine(modsDir, $"nrc-{profile.Version}-{loaderName}-{shortId}"),
                Path.Combine(modsDir, $"nrc-{profile.Version}-{loaderName}"),
                modsDir
            }
            .FirstOrDefault(d => Directory.Exists(d) && Directory.GetFiles(d).Any(ForeignInstance.IsModFile));

        return new ForeignInstance
        {
            Launcher = "NoRiskClient",
            Name = name,
            GameDir = dir,
            GameDirMods = false,
            ExtraModsDir = mods,
            ModrinthMods = profile.Mods,
            Loader = loader,
            MinecraftVersion = profile.Version,
            LoaderVersion = profile.LoaderVersion,
            Hint = "Die Mods des NoRisk-Clients selbst kommen nicht mit. Mods von Modrinth werden neu heruntergeladen" +
                   (mods != null ? ", eigene Mod-Dateien kopiert." : ".")
        };
    }

    private static bool IsLegacyMinecraft(string? version)
    {
        var parts = (version ?? "").Split('.');
        return parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var minor) && minor < 13;
    }

    private static IEnumerable<ForeignInstance> DotMinecraftClient(string launcher, string marker)
    {
        if (!Directory.Exists(marker) || !Directory.Exists(DotMinecraft))
            yield break;
        yield return new ForeignInstance
        {
            Launcher = launcher,
            Name = launcher,
            GameDir = DotMinecraft,
            Shared = true,
            GameDirMods = false,
            Hint = $"{launcher} bringt seine Mods (Minimap, Zoom ...) selbst mit, die lassen sich nicht übertragen. " +
                   "Übernommen werden Welten, Ressourcenpakete, Shader, Server und Einstellungen."
        };
    }

    private static IEnumerable<ForeignInstance> Feather()
    {
        var root = Path.Combine(AppData, ".feather");
        if (!Directory.Exists(root) || !Directory.Exists(DotMinecraft))
            yield break;
        var userMods = Path.Combine(root, "user-mods");
        var any = false;
        if (Directory.Exists(userMods))
            foreach (var dir in Directory.GetDirectories(userMods))
            {
                var m = Regex.Match(Path.GetFileName(dir), @"^(.+)-fabric$", RegexOptions.IgnoreCase);
                if (!m.Success || Directory.GetFiles(dir, "*.jar").Length == 0)
                    continue;
                any = true;
                yield return new ForeignInstance
                {
                    Launcher = "Feather Client",
                    Name = $"Feather {m.Groups[1].Value}",
                    GameDir = DotMinecraft,
                    Shared = true,
                    GameDirMods = false,
                    ExtraModsDir = dir,
                    Loader = ForeignLoader.Fabric,
                    MinecraftVersion = m.Groups[1].Value,
                    Hint = "Feathers eigene Mods kommen nicht mit, nur die selbst hinzugefügten."
                };
            }
        if (!any)
            foreach (var instance in DotMinecraftClient("Feather Client", root))
                yield return instance;
    }

    private static IEnumerable<ForeignInstance> LabyMod()
    {
        var launcherRoot = Path.Combine(AppData, "LabyMod");
        if (JsonFiles.ReadObject(Path.Combine(launcherRoot, "instances.json"), lenient: true) is { } json
            && json.TryGetProperty("instances", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
                if (LabyModInstance(launcherRoot, entry) is { } instance)
                    yield return instance;
            yield break;
        }
        foreach (var instance in LabyModLegacy())
            yield return instance;
    }

    private static ForeignInstance? LabyModInstance(string launcherRoot, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || JsonFiles.String(entry, "uid") is not { } uid
            || uid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || uid.Contains(".."))
            return null;
        string? version = null;
        if (entry.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            version = components.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object && JsonFiles.String(c, "uid") == "net.minecraft")
                .Select(c => JsonFiles.String(c, "version"))
                .FirstOrDefault();

        var instanceDir = Path.Combine(launcherRoot, "instances", uid);
        var separate = entry.TryGetProperty("separateGameDirectory", out var sep) && sep.ValueKind == JsonValueKind.True;
        var gameDir = separate
            ? new[] { "gameDirectory", "game", "minecraft", ".minecraft" }.Select(d => Path.Combine(instanceDir, d))
                .FirstOrDefault(Directory.Exists)
            : null;

        var candidates = new List<string>();
        if (version != null)
            candidates.Add(Path.Combine(instanceDir, "loader", "fabric", version, "mods"));
        candidates.Add(Path.Combine(DotMinecraft, "labymod-neo", "instances", uid, "mods"));
        if (version != null)
            candidates.Add(Path.Combine(DotMinecraft, "labymod-neo", "fabric", version, "mods"));
        var mods = candidates.FirstOrDefault(d => Directory.Exists(d) && Directory.GetFiles(d, "*.jar").Length > 0);

        var hint = "LabyMod selbst und seine Addons kommen nicht mit" +
                   (mods != null ? ", die hinzugefügten Fabric-Mods schon." : ".");
        if (separate && gameDir == null)
            hint += " Der eigene Spielordner dieser Instanz wurde nicht gefunden, übernommen wird deshalb \".minecraft\".";
        return new ForeignInstance
        {
            Launcher = "LabyMod",
            Name = JsonFiles.String(entry, "name") ?? $"LabyMod {version}",
            GameDir = gameDir ?? DotMinecraft,
            Shared = gameDir == null,
            GameDirMods = false,
            ExtraModsDir = mods,
            ExtraConfigDir = mods != null ? Path.Combine(Path.GetDirectoryName(mods)!, "config") : null,
            Loader = mods != null ? ForeignLoader.Fabric : ForeignLoader.Vanilla,
            MinecraftVersion = version,
            Hint = hint
        };
    }

    private static IEnumerable<ForeignInstance> LabyModLegacy()
    {
        var root = Path.Combine(DotMinecraft, "labymod-neo");
        if (!Directory.Exists(root))
            yield break;
        var fabric = Path.Combine(root, "fabric");
        var any = false;
        if (Directory.Exists(fabric))
            foreach (var dir in Directory.GetDirectories(fabric))
            {
                var mods = Path.Combine(dir, "mods");
                if (!Directory.Exists(mods) || Directory.GetFiles(mods, "*.jar").Length == 0)
                    continue;
                any = true;
                var version = Path.GetFileName(dir);
                yield return new ForeignInstance
                {
                    Launcher = "LabyMod",
                    Name = $"LabyMod {version}",
                    GameDir = DotMinecraft,
                    Shared = true,
                    GameDirMods = false,
                    ExtraModsDir = mods,
                    Loader = ForeignLoader.Fabric,
                    MinecraftVersion = version,
                    Hint = "LabyMod selbst und seine Addons kommen nicht mit, nur die hinzugefügten Fabric-Mods."
                };
            }
        if (!any)
            foreach (var instance in DotMinecraftClient("LabyMod", root))
                yield return instance;
    }

    private static void Count(ForeignInstance instance)
    {
        var saves = Path.Combine(instance.GameDir, "saves");
        instance.Worlds = WorldStore.Count(saves);
        var mods = instance.ModsDir;
        instance.Mods = (mods != null && Directory.Exists(mods) ? Directory.GetFiles(mods).Count(ForeignInstance.IsModFile) : 0)
                        + instance.ModrinthMods.Count;
    }

    private static void FromLog(ForeignInstance instance)
    {
        var log = Path.Combine(instance.GameDir, "logs", "latest.log");
        if (!File.Exists(log))
            return;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var head = new StringBuilder();
            for (var i = 0; i < 300 && reader.ReadLine() is { } line; i++)
                head.AppendLine(line);
            var text = head.ToString();
            Match m;
            if ((m = Regex.Match(text, @"Loading Minecraft (\S+) with Fabric Loader (\S+)")).Success)
                Apply(instance, ForeignLoader.Fabric, m.Groups[1].Value, m.Groups[2].Value);
            else if ((m = Regex.Match(text, @"Loading Minecraft (\S+) with Quilt Loader (\S+)")).Success)
                Apply(instance, ForeignLoader.Quilt, m.Groups[1].Value, m.Groups[2].Value);
            else if ((m = Regex.Match(text, @"--fml\.neoForgeVersion, ([^,\]\s]+).*?--fml\.mcVersion, ([^,\]\s]+)")).Success)
                Apply(instance, ForeignLoader.NeoForge, m.Groups[2].Value, m.Groups[1].Value);
            else if ((m = Regex.Match(text, @"--fml\.mcVersion, ([^,\]\s]+).*?--fml\.forgeVersion, ([^,\]\s]+)")).Success)
                Apply(instance, ForeignLoader.Forge, m.Groups[1].Value, m.Groups[2].Value);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Spielprotokoll einer fremden Instanz lesen", ex);
        }
    }

    private static void Apply(ForeignInstance instance, ForeignLoader loader, string version, string loaderVersion)
    {
        if (instance.Loader == ForeignLoader.Unknown)
        {
            instance.Loader = loader;
            instance.LoaderVersion ??= loaderVersion;
        }
        instance.MinecraftVersion ??= version;
    }

    private static ForeignLoader ParseLoader(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "vanilla" or "" or "none" => ForeignLoader.Vanilla,
        "fabric" => ForeignLoader.Fabric,
        "forge" => ForeignLoader.Forge,
        "quilt" => ForeignLoader.Quilt,
        "neoforge" or "neo_forge" or "neoforged" => ForeignLoader.NeoForge,
        _ => ForeignLoader.Unknown
    };

    private static IEnumerable<string> JsonStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

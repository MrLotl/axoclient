using System.IO.Compression;
using System.Text.Json;

namespace AxoClient.Content;

public class PackRepair(ContentStore store)
{
    private Installation Inst => store.Installation;

    private string OptionsPath => Inst.OptionsFile;

    public async Task<List<Issue>> AnalyzeAsync(ContentType type, IProgress<string> status)
    {
        var issues = new List<Issue>();
        status.Report("Lese die Dateien...");
        var items = store.GetInstalled(type);
        var packs = await Task.Run(() => items.Select(Read).ToList());

        foreach (var pack in packs.Where(p => p.Error != null))
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Error,
                Title = $"{pack.Item.DisplayName} ist nicht lesbar",
                Description = pack.Error + " Minecraft überspringt solche Dateien wortlos."
            });

        if (type == ContentType.ResourcePack)
        {
            CheckActivation(packs, issues);
            CheckPackFormats(packs, issues);
        }
        else
        {
            CheckShaderSetup(packs, issues);
        }

        status.Report("Prüfe, welche Mods gebraucht werden...");
        CheckRequiredMods(packs, type, issues);

        return issues.OrderBy(i => i.Severity).ToList();
    }

    private record Pack(InstalledItem Item, int? Format, string? Error, IReadOnlyList<string> Entries)
    {
        public bool Has(string part) => Entries.Any(e => e.Contains(part, StringComparison.Ordinal));

        public bool HasFileEnding(string ending) => Entries.Any(e => e.EndsWith(ending, StringComparison.Ordinal));
    }

    private static Pack Read(InstalledItem item)
    {
        try
        {
            var (meta, entries) = Directory.Exists(item.FullPath) ? ReadFolder(item.FullPath) : ReadZip(item.FullPath);
            if (meta == null)
                return new Pack(item, null, "Die Datei enthält keine pack.mcmeta.", entries);
            return new Pack(item, ReadFormat(meta), null, entries);
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Paket \"{item.FileName}\" prüfen", ex);
            return new Pack(item, null, ErrorReport.Short(ex), []);
        }
    }

    private const int MaxEntries = 40000;

    private static (string? Meta, List<string> Entries) ReadZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries.Take(MaxEntries).Select(e => e.FullName.Replace('\\', '/').ToLowerInvariant()).ToList();
        var meta = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/')
            .EndsWith("pack.mcmeta", StringComparison.OrdinalIgnoreCase));
        if (meta == null)
            return (null, entries);
        using var reader = new StreamReader(meta.Open());
        return (reader.ReadToEnd(), entries);
    }

    private static (string? Meta, List<string> Entries) ReadFolder(string dir)
    {
        var entries = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(MaxEntries)
            .Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/').ToLowerInvariant()).ToList();
        var meta = Path.Combine(dir, "pack.mcmeta");
        return (File.Exists(meta) ? File.ReadAllText(meta) : null, entries);
    }

    private static int? ReadFormat(string meta)
    {
        try
        {
            using var doc = JsonDocument.Parse(meta);
            if (!doc.RootElement.TryGetProperty("pack", out var pack))
                return null;
            return pack.TryGetProperty("pack_format", out var format) && format.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException ex)
        {
            ErrorReport.Log("pack.mcmeta lesen", ex);
            return null;
        }
    }

    private void CheckActivation(List<Pack> packs, List<Issue> issues)
    {
        var active = MinecraftOptions.ReadList(OptionsPath, MinecraftOptions.ResourcePacks);
        if (active == null)
        {
            if (packs.Count > 0)
                issues.Add(new Issue
                {
                    Severity = IssueSeverity.Info,
                    Title = "Der Launcher weiß nicht, welche Pakete eingeschaltet sind",
                    Description = File.Exists(OptionsPath)
                        ? "In der options.txt dieser Instanz steht noch keine Paketliste."
                        : "Diese Instanz hat noch keine options.txt. Starte das Spiel einmal, dann kann der " +
                          "Launcher Pakete auch von hier aus ein- und ausschalten."
                });
            return;
        }

        foreach (var pack in packs.Where(p => p.Error == null && !IsActive(active, p.Item.FileName)))
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Info,
                Title = $"{pack.Item.DisplayName} ist nicht eingeschaltet",
                Description = "Das Paket liegt im Ordner, steht aber nicht in den Spieleinstellungen. " +
                              "Solange es nicht eingeschaltet ist, ändert es nichts.",
                FixText = "Einschalten (wirkt beim nächsten Start)",
                Selected = false,
                Fix = Issue.Do(() =>
                {
                    var list = MinecraftOptions.ReadList(OptionsPath, MinecraftOptions.ResourcePacks) ?? ["vanilla"];
                    list.Add("file/" + pack.Item.FileName);
                    MinecraftOptions.WriteListOrThrow(OptionsPath, MinecraftOptions.ResourcePacks, list);
                })
            });

        var known = packs.Select(p => p.Item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dangling = active.Where(entry => !entry.Equals("vanilla", StringComparison.OrdinalIgnoreCase)
                                             && !known.Contains(MinecraftOptions.PackFileName(entry))).ToList();
        if (dangling.Count > 0)
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Warning,
                Title = $"{dangling.Count} eingeschaltete(s) Paket(e) fehlen im Ordner",
                Description = "In den Spieleinstellungen stehen: " + string.Join(", ", dangling) +
                              ". Die Dateien gibt es nicht mehr.",
                FixText = "Einträge entfernen",
                Fix = Issue.Do(() =>
                {
                    var list = MinecraftOptions.ReadList(OptionsPath, MinecraftOptions.ResourcePacks) ?? [];
                    list.RemoveAll(dangling.Contains);
                    MinecraftOptions.WriteListOrThrow(OptionsPath, MinecraftOptions.ResourcePacks, list);
                })
            });

        var incompatible = MinecraftOptions.ReadList(OptionsPath, MinecraftOptions.IncompatibleResourcePacks) ?? [];
        foreach (var pack in packs.Where(p => incompatible.Any(e => MinecraftOptions.PackFileName(e)
                     .Equals(p.Item.FileName, StringComparison.OrdinalIgnoreCase))))
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Warning,
                Title = $"Minecraft hält {pack.Item.DisplayName} für nicht passend",
                Description = $"Das Spiel hat dieses Paket zuletzt als inkompatibel zu {Inst.MinecraftVersion} " +
                              "eingestuft. Es lässt sich trotzdem einschalten, kann aber fehlerhaft aussehen. " +
                              "Sieh nach, ob es das Paket für diese Minecraft-Version gibt."
            });
    }

    private static bool IsActive(List<string> active, string fileName) =>
        active.Any(entry => MinecraftOptions.PackFileName(entry).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    private static void CheckPackFormats(List<Pack> packs, List<Issue> issues)
    {
        var formats = packs.Where(p => p.Format is > 0).Select(p => p.Format!.Value).OrderBy(f => f).ToList();
        if (formats.Count < 3)
            return;

        var typical = formats[formats.Count / 2];
        foreach (var pack in packs.Where(p => p.Format is > 0 && p.Format.Value * 2 <= typical))
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Info,
                Title = $"{pack.Item.DisplayName} ist vermutlich für ein älteres Minecraft",
                Description = $"Das Paket gibt pack_format {pack.Format} an, die anderen Pakete dieser Instanz " +
                              $"überwiegend {typical}. Texturen können fehlen oder an falschen Stellen landen."
            });
    }

    private void CheckShaderSetup(List<Pack> packs, List<Issue> issues)
    {
        var loaderMod = Inst.Loader == LoaderType.Forge ? ("Oculus", "oculus") : ("Iris Shaders", "iris");
        var hasIris = store.HasModMatching("iris", "oculus");

        if (!hasIris && Inst.Loader == LoaderType.Vanilla)
        {
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Error,
                Title = "Shader brauchen eine Mod",
                Description = "Diese Instanz läuft ohne Mod-Loader. Shaderpakete funktionieren nur mit Iris " +
                              "(Fabric) oder Oculus (Forge); dafür muss die Instanz Fabric oder Forge nutzen."
            });
        }
        else if (!hasIris)
        {
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Error,
                Title = $"{loaderMod.Item1} fehlt",
                Description = $"Ohne {loaderMod.Item1} lädt Minecraft keine Shaderpakete. " +
                              (packs.Count > 0 ? $"Im Ordner liegen bereits {packs.Count} Paket(e)." : ""),
                FixText = $"{loaderMod.Item1} installieren",
                Fix = Issue.Do(progress => store.InstallBySlugAsync(loaderMod.Item2, ContentType.Mod, progress.Text))
            });
        }

        foreach (var pack in packs.Where(p => p.Error == null && !p.Has("shaders/")))
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Warning,
                Title = $"{pack.Item.DisplayName} sieht nicht wie ein Shaderpaket aus",
                Description = "In der Datei fehlt der Ordner \"shaders\". Vielleicht ist es in Wirklichkeit ein " +
                              "Ressourcenpaket und liegt im falschen Ordner."
            });

        var selected = IrisConfig.ReadShader(Inst);
        if (selected is { Length: > 0 }
            && !packs.Any(p => p.Item.FileName.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Warning,
                Title = $"Eingestellt ist \"{selected}\", das Paket fehlt aber",
                Description = "Iris startet dann ohne Shader. Wähle im Spiel ein vorhandenes Paket oder schalte " +
                              "Shader aus.",
                FixText = "Shader ausschalten",
                Fix = Issue.Do(() =>
                {
                    if (!IrisConfig.WriteShader(Inst, ""))
                        throw new InvalidOperationException("Die Datei config/iris.properties konnte nicht geschrieben werden.");
                })
            });

        if (packs.Count > 0 && selected is { Length: 0 })
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Info,
                Title = "Es ist kein Shader eingeschaltet",
                Description = "Die Pakete liegen bereit, Iris ist aber auf \"aus\" gestellt. Einschalten geht im " +
                              "Spiel unter Optionen - Grafik - Shaderpakete, oder über ein Paket-Profil."
            });
    }

    private record ModNeed(string Feature, string Title, string Slug, string[] Installed, Func<Pack, bool> Detect);

    private static readonly ModNeed[] Needs =
    [
        new("eigene Texturen je Gegenstand (CIT)", "CIT Resewn", "cit-resewn", ["citresewn", "cit-resewn", "optifine"],
            p => p.Has("/optifine/cit/") || p.Has("/citresewn/")),
        new("verbundene Texturen (CTM)", "Continuity", "continuity", ["continuity", "optifine", "athena"],
            p => p.Has("/optifine/ctm/") || p.Has("/continuity/")),
        new("leuchtende Texturen und zufällige Mobs", "Entity Texture Features", "entitytexturefeatures",
            ["entitytexturefeatures", "entity_texture_features", "etf", "optifine"],
            p => p.Has("/optifine/random/") || p.Has("/optifine/mob/") || p.HasFileEnding("_e.png")),
        new("eigene Mob-Modelle (CEM)", "Entity Model Features", "entity-model-features",
            ["entity_model_features", "entitymodelfeatures", "emf", "optifine"],
            p => p.HasFileEnding(".jem") || p.HasFileEnding(".jpm")),
        new("3D-Texturen für Shader (PBR)", "Iris Shaders", "iris", ["iris", "oculus"],
            p => p.HasFileEnding("_n.png") || p.HasFileEnding("_s.png"))
    ];

    private void CheckRequiredMods(List<Pack> packs, ContentType type, List<Issue> issues)
    {
        if (type != ContentType.ResourcePack)
            return;

        foreach (var need in Needs)
        {
            var affected = packs.Where(p => p.Error == null && need.Detect(p)).ToList();
            if (affected.Count == 0 || store.HasModMatching(need.Installed))
                continue;

            var names = string.Join(", ", affected.Select(p => p.Item.DisplayName));
            if (Inst.Loader == LoaderType.Vanilla)
            {
                issues.Add(new Issue
                {
                    Severity = IssueSeverity.Warning,
                    Title = $"{names}: {need.Feature} braucht eine Mod",
                    Description = $"Dafür ist {need.Title} nötig, das gibt es aber nur für Fabric oder Forge. " +
                                  "Diese Instanz läuft ohne Mod-Loader; der Rest des Pakets funktioniert trotzdem."
                });
                continue;
            }

            var forge = Inst.Loader == LoaderType.Forge;
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Warning,
                Title = $"{need.Title} fehlt für {names}",
                Description = $"Das Paket bringt {need.Feature} mit. Ohne die passende Mod zeigt Minecraft dafür " +
                              "die normalen Texturen - das Paket sieht dann nur teilweise richtig aus." +
                              (forge ? " Unter Forge übernimmt das üblicherweise OptiFine." : ""),
                FixText = forge ? null : $"{need.Title} installieren",
                Fix = forge
                    ? null
                    : Issue.Do(progress => store.InstallBySlugAsync(need.Slug, ContentType.Mod, progress.Text))
            });
        }
    }
}

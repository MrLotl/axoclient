using System.IO;

namespace McLauncher;

public enum IssueSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>Ein gefundenes Problem mit optionaler automatischer Lösung.</summary>
public class RepairIssue
{
    public required IssueSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>Was die Lösung tut (null = nur Hinweis, keine automatische Lösung).</summary>
    public string? FixText { get; init; }
    public Func<IProgress<string>, Task>? Fix { get; init; }

    public bool CanFix => Fix != null;
    public bool Selected { get; set; } = true;

    public string Icon => Severity switch
    {
        IssueSeverity.Error => "",   // Fehler
        IssueSeverity.Warning => "", // Warnung
        _ => ""                      // Info
    };
}

/// <summary>
/// Prüft, ob die Mods einer Instanz zusammenpassen: Version/Loader, fehlende Abhängigkeiten,
/// doppelte Mods und gegenseitig inkompatible Mods. Mods werden über ihren Datei-Hash bei Modrinth
/// erkannt (auch manuell hinzugefügte), über den Launcher installierte CurseForge-Mods über ihre Datei-ID.
/// </summary>
public class ModRepair(ContentStore store, ModrinthProvider modrinth, CurseForgeProvider? curseForge)
{
    private record ModInfo(InstalledItem Item, ContentVersion? Version, string Title);

    private Installation Inst => store.Installation;

    public async Task<List<RepairIssue>> AnalyzeAsync(IProgress<string> status)
    {
        var issues = new List<RepairIssue>();
        if (Inst.Loader == LoaderType.Vanilla)
            return issues;

        status.Report("Erkenne Mods...");
        await store.IdentifyUnknownAsync(ContentType.Mod, modrinth);
        var items = store.GetInstalled(ContentType.Mod);

        // Version jeder Mod ermitteln: Modrinth per Hash, CurseForge per gespeicherter Datei-ID
        var hashes = items.Where(i => File.Exists(i.FullPath)).ToDictionary(i => i, i => ContentStore.Sha1(i.FullPath));
        var byHash = await modrinth.LookupByHashAsync(hashes.Values);
        var mods = new List<ModInfo>();
        foreach (var item in items)
        {
            ContentVersion? version = hashes.TryGetValue(item, out var h) && byHash.TryGetValue(h, out var mv) ? mv : null;
            if (version == null && item.Entry is { Source: ContentSource.CurseForge, VersionId: { } fileId } entry && curseForge != null)
            {
                status.Report($"Prüfe {item.DisplayName}...");
                version = await curseForge.GetVersionAsync(entry.ProjectId, fileId);
            }
            mods.Add(new ModInfo(item, version, item.DisplayName));
        }

        var enabled = mods.Where(m => m.Item.Enabled).ToList();
        // Projekt-IDs beider Plattformen: eine Mod kann per Hash als Modrinth-Projekt erkannt werden,
        // obwohl sie über CurseForge installiert wurde (und umgekehrt als Abhängigkeit gesucht werden)
        var installedIds = enabled.Where(m => m.Version != null).Select(m => m.Version!.ProjectId)
            .Concat(enabled.Where(m => m.Item.Entry != null).Select(m => m.Item.Entry!.ProjectId))
            .ToHashSet();

        status.Report("Prüfe Kompatibilität...");
        CheckDuplicates(enabled, issues);
        await CheckVersionsAsync(enabled, issues);
        await CheckDependenciesAsync(enabled, mods, installedIds, issues);
        CheckIncompatibilities(enabled, installedIds, issues);

        var unknown = enabled.Where(m => m.Version == null).Select(m => m.Item.FileName).ToList();
        if (unknown.Count > 0)
            issues.Add(new RepairIssue
            {
                Severity = IssueSeverity.Info,
                Title = $"{unknown.Count} Mod(s) konnten nicht geprüft werden",
                Description = "Weder Modrinth noch der Launcher kennen diese Dateien: " + string.Join(", ", unknown)
            });

        return issues
            .OrderBy(i => i.Severity)
            .ToList();
    }

    /// <summary>Dasselbe Projekt mehrfach aktiv: nur die neueste Version behalten.</summary>
    private void CheckDuplicates(List<ModInfo> enabled, List<RepairIssue> issues)
    {
        foreach (var group in enabled.Where(m => m.Version != null).GroupBy(m => m.Version!.ProjectId).Where(g => g.Count() > 1))
        {
            var ordered = group.OrderByDescending(m => m.Version!.Date).ToList();
            var keep = ordered[0];
            var remove = ordered.Skip(1).ToList();
            issues.Add(new RepairIssue
            {
                Severity = IssueSeverity.Error,
                Title = $"{keep.Title} ist mehrfach installiert",
                Description = "Mehrere Versionen derselben Mod führen zu einem Absturz beim Start: " +
                              string.Join(", ", group.Select(m => m.Item.FileName)),
                FixText = $"Nur {keep.Version!.Name} behalten, ältere deaktivieren",
                Fix = _ =>
                {
                    foreach (var mod in remove)
                        store.SetEnabled(mod.Item, false);
                    return Task.CompletedTask;
                }
            });
        }
    }

    /// <summary>Mod passt nicht zur Minecraft-Version oder zum Loader der Instanz.</summary>
    private async Task CheckVersionsAsync(List<ModInfo> enabled, List<RepairIssue> issues)
    {
        foreach (var mod in enabled.Where(m => m.Version != null && !m.Version.Supports(Inst, ContentType.Mod)))
        {
            var version = mod.Version!;
            IContentProvider? provider = version.Source == ContentSource.Modrinth ? modrinth : curseForge;
            ContentVersion? replacement = null;
            try
            {
                replacement = provider == null ? null : await provider.GetLatestVersionAsync(version.ProjectId, ContentType.Mod, Inst);
            }
            catch
            {
                // Ersatz nicht ermittelbar: dann wird deaktiviert
            }

            var madeFor = string.Join(", ", version.Loaders) + " " +
                          string.Join(", ", version.GameVersions.TakeLast(3));
            issues.Add(new RepairIssue
            {
                Severity = IssueSeverity.Error,
                Title = $"{mod.Title} passt nicht zu {Inst.Loader} {Inst.MinecraftVersion}",
                Description = $"Die installierte Version {version.Name} ist für {madeFor.Trim()}.",
                FixText = replacement != null
                    ? $"Durch passende Version {replacement.Name} ersetzen"
                    : "Deaktivieren (keine passende Version verfügbar)",
                Fix = replacement != null
                    ? async status => await ReplaceAsync(provider!, mod, replacement, status)
                    : _ =>
                    {
                        store.SetEnabled(mod.Item, false);
                        return Task.CompletedTask;
                    }
            });
        }
    }

    private async Task ReplaceAsync(IContentProvider provider, ModInfo mod, ContentVersion replacement, IProgress<string> status)
    {
        // Manuell installierte Datei zuerst entfernen, damit keine zwei Versionen übrig bleiben
        if (mod.Item.Entry == null || mod.Item.Entry.Source != provider.Source)
            store.Delete(mod.Item);
        await store.InstallAsync(provider, replacement.ProjectId, mod.Title, ContentType.Mod, status,
            version: replacement, replace: true);
    }

    /// <summary>Benötigte Mods fehlen (oder sind nur deaktiviert).</summary>
    private async Task CheckDependenciesAsync(List<ModInfo> enabled, List<ModInfo> all, HashSet<string> installedIds,
        List<RepairIssue> issues)
    {
        var missing = enabled
            .Where(m => m.Version != null)
            .SelectMany(m => m.Version!.RequiredProjectIds.Select(dep => (Dependency: dep, Mod: m)))
            .Where(x => !installedIds.Contains(x.Dependency))
            .GroupBy(x => x.Dependency);

        foreach (var group in missing)
        {
            var dependencyId = group.Key;
            var neededBy = string.Join(", ", group.Select(x => x.Mod.Title).Distinct());
            var source = group.First().Mod.Version!.Source;

            // Nur deaktiviert? Dann einfach wieder einschalten
            var disabled = all.FirstOrDefault(m => !m.Item.Enabled && m.Version?.ProjectId == dependencyId);
            if (disabled != null)
            {
                issues.Add(new RepairIssue
                {
                    Severity = IssueSeverity.Error,
                    Title = $"{disabled.Title} ist deaktiviert, wird aber benötigt",
                    Description = $"Benötigt von: {neededBy}",
                    FixText = $"{disabled.Title} aktivieren",
                    Fix = _ =>
                    {
                        store.SetEnabled(disabled.Item, true);
                        return Task.CompletedTask;
                    }
                });
                continue;
            }

            IContentProvider? provider = source == ContentSource.Modrinth ? modrinth : curseForge;
            var title = dependencyId;
            try
            {
                if (provider != null)
                    title = (await provider.GetProjectInfoAsync(dependencyId)).Title;
            }
            catch
            {
                // Titel unbekannt: ID anzeigen
            }

            issues.Add(new RepairIssue
            {
                Severity = IssueSeverity.Error,
                Title = $"{title} fehlt",
                Description = $"Wird benötigt von: {neededBy}",
                FixText = provider != null ? $"{title} installieren" : null,
                Fix = provider != null
                    ? async status => await store.InstallAsync(provider, dependencyId, title, ContentType.Mod, status)
                    : null
            });
        }
    }

    /// <summary>Zwei aktive Mods schließen sich laut Autor gegenseitig aus.</summary>
    private void CheckIncompatibilities(List<ModInfo> enabled, HashSet<string> installedIds, List<RepairIssue> issues)
    {
        foreach (var mod in enabled.Where(m => m.Version != null))
        {
            foreach (var other in mod.Version!.IncompatibleProjectIds.Where(installedIds.Contains))
            {
                var otherMod = enabled.FirstOrDefault(m => m.Version?.ProjectId == other || m.Item.Entry?.ProjectId == other);
                if (otherMod == null || otherMod == mod)
                    continue;
                issues.Add(new RepairIssue
                {
                    Severity = IssueSeverity.Warning,
                    Title = $"{mod.Title} verträgt sich nicht mit {otherMod.Title}",
                    Description = "Der Autor gibt an, dass beide Mods nicht zusammen funktionieren.",
                    FixText = $"{mod.Title} deaktivieren",
                    Selected = false, // Welche weg soll, entscheidet der Nutzer
                    Fix = _ =>
                    {
                        store.SetEnabled(mod.Item, false);
                        return Task.CompletedTask;
                    }
                });
            }
        }
    }
}

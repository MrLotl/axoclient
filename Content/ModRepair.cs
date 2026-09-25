namespace AxoClient.Content;

public class ModRepair(ContentStore store, ModrinthClient modrinth)
{
    private record ModInfo(InstalledItem Item, ContentVersion? Version, string Title);

    private Installation Inst => store.Installation;

    public async Task<List<Issue>> AnalyzeAsync(IProgress<string> status)
    {
        var issues = new List<Issue>();
        if (!Inst.CanUseMods)
            return issues;

        status.Report("Erkenne Mods...");
        await store.IdentifyUnknownAsync(ContentType.Mod);
        var items = store.GetInstalled(ContentType.Mod);

        var hashes = items.Where(i => File.Exists(i.FullPath)).ToDictionary(i => i, i => FileOps.Sha1(i.FullPath));
        var byHash = await modrinth.LookupByHashAsync(hashes.Values);
        var mods = items
            .Select(item => new ModInfo(item,
                hashes.TryGetValue(item, out var hash) && byHash.TryGetValue(hash, out var version) ? version : null,
                item.DisplayName))
            .ToList();

        var enabled = mods.Where(m => m.Item.Enabled).ToList();
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
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Info,
                Title = $"{unknown.Count} Mod(s) konnten nicht geprüft werden",
                Description = "Weder Modrinth noch der Launcher kennen diese Dateien: " + string.Join(", ", unknown)
            });

        return issues.OrderBy(i => i.Severity).ToList();
    }

    private void CheckDuplicates(List<ModInfo> enabled, List<Issue> issues)
    {
        foreach (var group in enabled.Where(m => m.Version != null).GroupBy(m => m.Version!.ProjectId).Where(g => g.Count() > 1))
        {
            var ordered = group.OrderByDescending(m => m.Version!.Date).ToList();
            var keep = ordered[0];
            var remove = ordered.Skip(1).ToList();
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Error,
                Title = $"{keep.Title} ist mehrfach installiert",
                Description = "Mehrere Versionen derselben Mod führen zu einem Absturz beim Start: " +
                              string.Join(", ", group.Select(m => m.Item.FileName)),
                FixText = $"Nur {keep.Version!.Name} behalten, ältere deaktivieren",
                Fix = Issue.Do(() => remove.ForEach(mod => store.SetEnabled(mod.Item, false)))
            });
        }
    }

    private async Task CheckVersionsAsync(List<ModInfo> enabled, List<Issue> issues)
    {
        foreach (var mod in enabled.Where(m => m.Version != null && !m.Version.Supports(Inst, ContentType.Mod)))
        {
            var version = mod.Version!;
            ContentVersion? replacement = null;
            try
            {
                replacement = await modrinth.GetLatestVersionAsync(version.ProjectId, ContentType.Mod, Inst);
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Ersatz für eine Mod suchen", ex);
            }

            var madeFor = string.Join(", ", version.Loaders) + " " + string.Join(", ", version.GameVersions.TakeLast(3));
            issues.Add(new Issue
            {
                Severity = IssueSeverity.Error,
                Title = $"{mod.Title} passt nicht zu {Inst.Loader} {Inst.MinecraftVersion}",
                Description = $"Die installierte Version {version.Name} ist für {madeFor.Trim()}.",
                FixText = replacement != null
                    ? $"Durch passende Version {replacement.Name} ersetzen"
                    : "Deaktivieren (keine passende Version verfügbar)",
                Fix = replacement != null
                    ? Issue.Do(progress => ReplaceAsync(mod, replacement, progress.Text))
                    : Issue.Do(() => store.SetEnabled(mod.Item, false))
            });
        }
    }

    private async Task ReplaceAsync(ModInfo mod, ContentVersion replacement, IProgress<string> status)
    {
        if (mod.Item.Entry is not { IsFromModrinth: true })
            store.Delete(mod.Item);
        await store.InstallAsync(replacement.ProjectId, mod.Title, ContentType.Mod, status, version: replacement, replace: true);
    }

    private async Task CheckDependenciesAsync(List<ModInfo> enabled, List<ModInfo> all, HashSet<string> installedIds,
        List<Issue> issues)
    {
        var missing = enabled
            .Where(m => m.Version != null)
            .SelectMany(m => m.Version!.RequiredProjectIds.Select(dependency => (Dependency: dependency, Mod: m)))
            .Where(x => !installedIds.Contains(x.Dependency))
            .GroupBy(x => x.Dependency);

        foreach (var group in missing)
        {
            var dependencyId = group.Key;
            var neededBy = string.Join(", ", group.Select(x => x.Mod.Title).Distinct());

            if (all.FirstOrDefault(m => !m.Item.Enabled && m.Version?.ProjectId == dependencyId) is { } disabled)
            {
                issues.Add(new Issue
                {
                    Severity = IssueSeverity.Error,
                    Title = $"{disabled.Title} ist deaktiviert, wird aber benötigt",
                    Description = $"Benötigt von: {neededBy}",
                    FixText = $"{disabled.Title} aktivieren",
                    Fix = Issue.Do(() => store.SetEnabled(disabled.Item, true))
                });
                continue;
            }

            var title = dependencyId;
            try
            {
                title = (await modrinth.GetProjectInfoAsync(dependencyId)).Title;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Titel einer Mod abfragen", ex);
            }

            issues.Add(new Issue
            {
                Severity = IssueSeverity.Error,
                Title = $"{title} fehlt",
                Description = $"Wird benötigt von: {neededBy}",
                FixText = $"{title} installieren",
                Fix = Issue.Do(progress => store.InstallAsync(dependencyId, title, ContentType.Mod, progress.Text))
            });
        }
    }

    private void CheckIncompatibilities(List<ModInfo> enabled, HashSet<string> installedIds, List<Issue> issues)
    {
        foreach (var mod in enabled.Where(m => m.Version != null))
        {
            foreach (var other in mod.Version!.IncompatibleProjectIds.Where(installedIds.Contains))
            {
                var otherMod = enabled.FirstOrDefault(m => m.Version?.ProjectId == other || m.Item.Entry?.ProjectId == other);
                if (otherMod == null || otherMod == mod)
                    continue;
                issues.Add(new Issue
                {
                    Severity = IssueSeverity.Warning,
                    Title = $"{mod.Title} verträgt sich nicht mit {otherMod.Title}",
                    Description = "Der Autor gibt an, dass beide Mods nicht zusammen funktionieren.",
                    FixText = $"{mod.Title} deaktivieren",
                    Selected = false,
                    Fix = Issue.Do(() => store.SetEnabled(mod.Item, false))
                });
            }
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class UpgradeDialog
{
    public static async Task<Installation?> RunAsync(AppServices app, Installation inst)
    {
        if (app.Games.IsRunning(inst))
        {
            await app.Dialogs.ShowMessageAsync("Minecraft läuft",
                $"\"{inst.Name}\" läuft gerade. Beende das Spiel, bevor du die Instanz hochziehst.");
            return null;
        }

        if (await AskTargetAsync(app, inst) is not { } target)
            return null;
        var (version, loader) = target;

        var plan = await UiRun.RunAsync(app, $"Prüfe Inhalte für Minecraft {version}",
            progress => InstanceUpgrade.PlanAsync(app, inst, version, loader, progress), "Prüfung fehlgeschlagen");
        return plan == null ? null : await ConfirmAndApplyAsync(app, plan);
    }

    private static async Task<(string Version, LoaderType Loader)?> AskTargetAsync(AppServices app, Installation inst)
    {
        const string group = "UpgradeLoader";
        var loaders = LoaderTypes.All.ToDictionary(l => l, l => Ui.Choice(l.ToString(), group, inst.Loader == l));
        var versions = Ui.Combo([]);
        var problem = Ui.Problem();
        var form = Ui.Stack(
            Ui.Note($"\"{inst.Name}\" läuft auf {inst.Loader} {inst.MinecraftVersion}. Der Launcher sucht für jeden Mod, " +
                    "jedes Ressourcenpaket und jeden Shader eine passende Version und legt daraus eine neue Instanz an. " +
                    "Diese Instanz bleibt unverändert.", 12),
            Ui.Label("Mod-Loader"), Ui.Row(loaders.Values.Cast<UIElement>().ToArray()),
            Ui.Label("Auf welche Minecraft-Version?"), versions, problem);

        LoaderType Chosen() => loaders.First(l => l.Value.IsChecked == true).Key;

        var loading = 0;

        async Task LoadVersionsAsync()
        {
            var request = ++loading;
            versions.IsEnabled = false;
            Ui.ShowProblem(problem, "Lade Versionsliste...");
            try
            {
                var list = await InstanceUpgrade.TargetVersionsAsync(app, inst, Chosen());
                if (request != loading)
                    return;
                versions.ItemsSource = list;
                versions.SelectedItem = list.FirstOrDefault();
                Ui.ShowProblem(problem, list.Count == 0 ? "Für diesen Mod-Loader gibt es keine neuere Version." : null);
            }
            catch (Exception ex)
            {
                if (request == loading)
                    Ui.ShowProblem(problem, "Versionsliste konnte nicht geladen werden: " + ErrorReport.Short(ex));
            }
            finally
            {
                if (request == loading)
                    versions.IsEnabled = true;
            }
        }

        foreach (var radio in loaders.Values)
            radio.Checked += (_, _) => _ = LoadVersionsAsync();
        _ = LoadVersionsAsync();

        if (!await app.Dialogs.ShowFormAsync("Instanz hochziehen", form, "Prüfen", () => versions.SelectedItem is string))
            return null;
        return ((string)versions.SelectedItem!, Chosen());
    }

    private static async Task<Installation?> ConfirmAndApplyAsync(AppServices app, UpgradePlan plan)
    {
        var nameBox = Ui.Input(app.Instances.UniqueName($"{plan.Source.Name} {plan.TargetVersion}"));
        var withPrereleases = plan.Prereleases.Count > 0
            ? Ui.Check($"Auch Beta-/Alpha-Versionen übernehmen ({plan.Prereleases.Count})")
            : null;
        var worlds = Ui.Check("Welten mitnehmen");
        var options = Ui.Check("Einstellungen und Tastenbelegung mitnehmen");
        var servers = Ui.Check("Serverliste mitnehmen");

        var form = Ui.Stack(
            Ui.Note($"Ergebnis für Minecraft {plan.TargetVersion}: {plan.Summary}."),
            Ui.Scroll(EntryList(plan), 210),
            Ui.Label("Name der neuen Instanz"), nameBox,
            withPrereleases, worlds, options, servers,
            Ui.Note($"\"{plan.Source.Name}\" bleibt so, wie sie ist. Die Welten werden kopiert, " +
                    "nicht verschoben – in der alten Instanz sind sie also weiter da.", 0, 11));

        if (!await app.Dialogs.ShowFormAsync("Neue Instanz anlegen", form, "Anlegen", () => nameBox.Text.Trim().Length > 0))
            return null;

        var carry = plan.Ready.ToList();
        if (Ui.IsChosen(withPrereleases))
            carry.AddRange(plan.Prereleases);
        var transfer = (Ui.IsChosen(worlds) ? TransferItems.Worlds : TransferItems.None)
                       | (Ui.IsChosen(options) ? TransferItems.Options : TransferItems.None)
                       | (Ui.IsChosen(servers) ? TransferItems.Servers : TransferItems.None);

        var name = nameBox.Text.Trim();
        var result = await UiRun.RunAsync(app, "Instanz wird hochgezogen",
            progress => InstanceUpgrade.ApplyAsync(app, plan, name, carry, transfer, progress), "Hochziehen fehlgeschlagen");
        if (result == null)
            return null;

        await UiRun.ShowReportAsync(app, "Instanz hochgezogen", result.Report);
        return result.Instance;
    }

    private static UIElement EntryList(UpgradePlan plan)
    {
        if (plan.Entries.Count == 0)
            return Ui.Note("Diese Instanz hat noch keine Mods, Pakete oder Shader.", 0, 12);

        var list = new StackPanel();
        foreach (var entry in plan.Entries)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var state = new TextBlock
            {
                Text = entry.StateText,
                FontSize = 12,
                Foreground = entry.State switch
                {
                    UpgradeState.Ready => Ui.GoodBrush,
                    UpgradeState.Prerelease => Ui.WarningBrush,
                    _ => Ui.ProblemBrush
                },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            DockPanel.SetDock(state, Dock.Right);
            row.Children.Add(state);

            var name = new TextBlock { Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines.Add(new Run(entry.Title));
            name.Inlines.Add(new Run($"  ·  {entry.TypeText}") { Foreground = Ui.Resource<Brush>("MutedText") });
            row.Children.Add(name);
            list.Children.Add(row);
        }
        return list;
    }
}

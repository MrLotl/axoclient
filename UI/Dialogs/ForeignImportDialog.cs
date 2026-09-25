using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class ForeignImportDialog
{
    private const double DialogWidth = 760;
    private const double ListHeight = 380;

    private sealed class Row
    {
        public required ForeignInstance Source { get; init; }
        public required CheckBox Box { get; init; }
        public ComboBox? LoaderBox { get; init; }
        public TextBox? VersionBox { get; init; }

        public bool Chosen => Box.IsChecked == true && Source.Importable;

        public LoaderType Loader => LoaderBox?.SelectedItem is LoaderType chosen
            ? chosen
            : ForeignInstance.AsLoaderType(Source.Loader) ?? LoaderType.Vanilla;

        public string Version => VersionBox?.Text.Trim() ?? Source.MinecraftVersion ?? "";
    }

    private sealed class Tab
    {
        public required ForeignLauncher Launcher { get; init; }
        public required RadioButton Button { get; init; }
        public FrameworkElement? Page { get; set; }
        public List<Row> Rows { get; } = [];
    }

    public static Task RunAsync(AppServices app) =>
        UiRun.GuardAsync(app, "Import aus anderem Launcher fehlgeschlagen", () => RunCoreAsync(app));

    private static async Task RunCoreAsync(AppServices app)
    {
        var launchers = await UiRun.RunAsync(app, "Suche andere Launcher",
            ForeignLaunchers.ScanAsync, "Suche fehlgeschlagen");
        if (launchers == null)
            return;
        if (!launchers.Any(l => l.Installed))
        {
            await app.Dialogs.ShowMessageAsync("Aus anderem Launcher",
                "Es wurde kein anderer Launcher gefunden.\n\nGesucht wird bei " +
                string.Join(", ", launchers.Select(l => l.Name)) + " – jeweils am Standard-Speicherort.");
            return;
        }

        var tabs = new List<Tab>();
        var nav = new StackPanel();
        var pages = new Grid();
        var summary = Ui.Note("", 0);
        var problem = Ui.Problem();

        void UpdateCounts()
        {
            foreach (var tab in tabs.Where(t => t.Launcher.Installed))
                SetNavText(tab);
            var chosen = tabs.Sum(t => t.Rows.Count(r => r.Chosen));
            summary.Text = chosen switch
            {
                0 => "Noch nichts ausgewählt.",
                1 => "1 Instanz ausgewählt.",
                _ => $"{chosen} Instanzen ausgewählt."
            };
        }

        void Show(Tab shown)
        {
            foreach (var tab in tabs)
            {
                if (tab.Page != null)
                    tab.Page.Visibility = tab == shown ? Visibility.Visible : Visibility.Collapsed;
                tab.Button.IsChecked = tab == shown;
            }
        }

        var installed = launchers.Where(l => l.Installed).ToList();
        var missing = launchers.Where(l => !l.Installed).ToList();
        foreach (var launcher in installed.Concat(missing))
        {
            if (launcher == missing.FirstOrDefault())
                nav.Children.Add(NavHeading("Nicht gefunden"));
            var button = new RadioButton
            {
                Style = Ui.Resource<Style>("NavButton"),
                GroupName = "ForeignLaunchers",
                Tag = launcher.Installed ? "" : "",
                IsEnabled = launcher.Installed,
                Opacity = launcher.Installed ? 1 : 0.4,
                ToolTip = launcher.Installed ? null : "Nicht auf diesem Rechner gefunden (am Standard-Speicherort)"
            };
            var tab = new Tab { Launcher = launcher, Button = button };
            if (launcher.Installed)
            {
                tab.Page = BuildPage(tab, UpdateCounts);
                pages.Children.Add(tab.Page);
                button.Checked += (_, _) => Show(tab);
            }
            else
                button.Content = new TextBlock { Text = launcher.Name };
            tabs.Add(tab);
            nav.Children.Add(button);
        }

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var navScroll = Ui.Scroll(nav, ListHeight + 40);
        navScroll.Margin = new Thickness(0, 0, 14, 0);
        body.Children.Add(navScroll);
        Grid.SetColumn(pages, 1);
        body.Children.Add(pages);

        var form = new StackPanel();
        form.Children.Add(Ui.Note("Gewählte Instanzen werden als neue AxoClient-Instanzen angelegt: Welten, Mods, " +
                                  "Ressourcenpakete, Shader, Server und Einstellungen werden kopiert. Im anderen " +
                                  "Launcher bleibt alles, wie es ist.", 12));
        form.Children.Add(body);
        summary.Margin = new Thickness(0, 10, 0, 0);
        form.Children.Add(summary);
        form.Children.Add(problem);

        UpdateCounts();
        Show(tabs.FirstOrDefault(t => t.Rows.Any(r => r.Source.Importable))
             ?? tabs.First(t => t.Launcher.Installed));

        bool Validate()
        {
            var chosen = tabs.SelectMany(t => t.Rows).Where(r => r.Chosen).ToList();
            return Ui.ShowProblem(problem, chosen.Count == 0 ? "Wähle mindestens eine Instanz."
                : chosen.FirstOrDefault(r => r.Version.Length == 0) is { } open
                    ? $"Trage bei \"{open.Source.Name}\" ({open.Source.Launcher}) die Minecraft-Version ein."
                    : null);
        }

        if (!await app.Dialogs.ShowFormAsync("Aus anderem Launcher importieren", form, "Importieren", Validate,
                DialogWidth))
            return;

        var choices = tabs.SelectMany(t => t.Rows).Where(r => r.Chosen)
            .Select(r => new ForeignImportChoice(r.Source, r.Source.Name, r.Loader, r.Version))
            .ToList();
        var report = await UiRun.RunAsync(app, "Instanzen werden übernommen",
            progress => new ForeignImporter(app).ImportAsync(choices, progress), "Import fehlgeschlagen");
        if (report != null)
            await UiRun.ShowReportAsync(app, "Import abgeschlossen", report);
    }

    private static TextBlock NavHeading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = Ui.Resource<Brush>("MutedText"),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(12, 10, 0, 6)
    };

    private static void SetNavText(Tab tab)
    {
        var importable = tab.Rows.Count(r => r.Source.Importable);
        var chosen = tab.Rows.Count(r => r.Chosen);
        var text = new TextBlock();
        text.Inlines.Add(new Run(tab.Launcher.Name));
        text.Inlines.Add(new Run(chosen > 0 ? $"  {chosen}/{importable}" : $"  {importable}")
        {
            Foreground = chosen > 0
                ? Ui.Resource<Brush>("Accent")
                : Ui.Resource<Brush>("MutedText"),
            FontSize = 12
        });
        tab.Button.Content = text;
    }

    private static FrameworkElement BuildPage(Tab tab, Action changed)
    {
        var page = new DockPanel { Visibility = Visibility.Collapsed };
        var list = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(header, Dock.Top);
        var all = new Button
        {
            Content = "Alle wählen",
            Style = Ui.Resource<Style>("LauncherButton"),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12
        };
        DockPanel.SetDock(all, Dock.Right);
        header.Children.Add(all);
        header.Children.Add(new TextBlock
        {
            Text = tab.Launcher.Name,
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        page.Children.Add(header);

        var scroll = Ui.Scroll(list, ListHeight);
        scroll.Height = ListHeight;
        scroll.Margin = new Thickness(0);
        page.Children.Add(scroll);

        all.Click += (_, _) =>
        {
            var rows = tab.Rows.Where(r => r.Source.Importable).ToList();
            var select = rows.Any(r => r.Box.IsChecked != true);
            foreach (var row in rows)
                row.Box.IsChecked = select;
        };

        if (tab.Launcher.Problem != null)
            list.Children.Add(Message(tab.Launcher.Problem, Ui.ProblemBrush));
        else if (tab.Launcher.Instances.Count == 0)
            list.Children.Add(Message("Keine Instanzen gefunden.",
                Ui.Resource<Brush>("SubtleText")));

        foreach (var source in tab.Launcher.Instances)
            list.Children.Add(BuildRow(source, tab.Rows, () =>
            {
                all.Content = tab.Rows.Where(r => r.Source.Importable).All(r => r.Box.IsChecked == true)
                    ? "Keine wählen"
                    : "Alle wählen";
                changed();
            }));
        all.IsEnabled = tab.Rows.Any(r => r.Source.Importable);
        return page;
    }

    private static TextBlock Message(string text, Brush brush) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 6, 0, 0)
    };

    private static FrameworkElement BuildRow(ForeignInstance source, List<Row> rows, Action changed)
    {
        var subtle = Ui.Resource<Brush>("SubtleText");
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = source.Name,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = source.Description,
            Foreground = subtle,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap
        });
        var box = new CheckBox { Content = text, IsEnabled = source.Importable };
        box.Checked += (_, _) => changed();
        box.Unchecked += (_, _) => changed();

        var panel = new StackPanel();
        panel.Children.Add(box);

        ComboBox? loaderBox = null;
        TextBox? versionBox = null;
        if (!source.Importable)
        {
            text.Opacity = 0.55;
            var reason = Ui.Note(source.Problem!, 0, 12.5);
            reason.Foreground = Ui.ProblemBrush;
            reason.Margin = new Thickness(26, 4, 4, 0);
            panel.Children.Add(reason);
        }
        else
        {
            if (!source.Complete)
            {
                loaderBox = Ui.Combo([LoaderType.Vanilla, LoaderType.Fabric, LoaderType.Forge]);
                loaderBox.SelectedItem = ForeignInstance.AsLoaderType(source.Loader)
                                         ?? (source.Loader == ForeignLoader.Unknown && source.Mods > 0
                                             ? LoaderType.Fabric
                                             : LoaderType.Vanilla);
                loaderBox.Width = 110;
                loaderBox.Margin = new Thickness(0, 0, 8, 0);
                versionBox = Ui.Input(source.MinecraftVersion ?? "");
                versionBox.Width = 110;
                versionBox.Margin = new Thickness(0);
                versionBox.ToolTip = "Minecraft-Version, z.B. 1.21.1";
                var edit = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 6, 0, 0) };
                edit.Children.Add(loaderBox);
                edit.Children.Add(versionBox);
                edit.Children.Add(new TextBlock
                {
                    Text = "Version",
                    Foreground = subtle,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                });
                panel.Children.Add(edit);
            }
            if (source.Hint != null)
            {
                var hint = Ui.Note(source.Hint, 0, 12.5);
                hint.Margin = new Thickness(26, 4, 4, 0);
                panel.Children.Add(hint);
            }
        }

        rows.Add(new Row { Source = source, Box = box, LoaderBox = loaderBox, VersionBox = versionBox });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 6),
            Child = panel
        };
    }
}

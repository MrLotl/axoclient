using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.InstanceTabs;

public static class ContentDialogs
{
    private record Category(string Label, string? Facet, string? Query = null);

    private static readonly Dictionary<ContentType, Category[]> Categories = new()
    {
        [ContentType.Mod] =
        [
            new("Alle", null), new("Leistung", "optimization"), new("Komfort", "utility"), new("Karte", null, "map"),
            new("Technik", "technology"), new("Bibliothek", "library")
        ],
        [ContentType.ResourcePack] =
        [
            new("Alle", null), new("Vanilla-Stil", "vanilla-like"), new("Realistisch", "realistic"),
            new("Oberfläche", "gui"), new("Animationen", null, "animated")
        ],
        [ContentType.Shader] =
        [
            new("Alle", null), new("Vanilla-Stil", "vanilla-like"), new("Realistisch", "realistic"),
            new("Fantasy", "fantasy"), new("Leistung", "low")
        ]
    };

    private static readonly (string Label, string Index)[] Sorts =
        [("Beliebteste", "downloads"), ("Neueste", "newest"), ("Zuletzt aktualisiert", "updated"), ("Name (A–Z)", "name")];

    private static string Plural(ContentType type) => ContentTypes.PluralLabel(type);

    private static string Target(Installation inst) => $"{inst.Name} · {InstanceText.Version(inst)}";

    public static async Task AddAsync(AppServices app, Installation inst, ContentStore store, ContentType type)
    {
        var pending = new Dictionary<string, ContentProject>();
        var installed = store.GetInstalled(type);
        var category = Categories[type][0];
        var sortIndex = 0;
        var page = 0;
        var request = 0;
        var working = false;

        var installedCount = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextSecondary"), VerticalAlignment = VerticalAlignment.Center };
        var leftFilter = new TextBox { Style = Ui.Resource<Style>("SearchBox"), Height = 34, Background = Ui.Resource<Brush>("DeepBg"), BorderBrush = Ui.Resource<Brush>("Divider"), FontSize = 12.5 };
        Field.SetHint(leftFilter, "Filtern …");
        var leftList = new StackPanel();
        var leftHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        leftHead.Children.Add(new TextBlock { Text = "Bereits installiert", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextPrimary"), VerticalAlignment = VerticalAlignment.Center });
        leftHead.Children.Add(new Border { Height = 20, Padding = new Thickness(7, 0, 7, 0), CornerRadius = new CornerRadius(10), Background = Ui.Resource<Brush>("CardHover"), Margin = new Thickness(8, 0, 0, 0), Child = installedCount });
        var leftScroll = new ScrollViewer { Content = leftList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, -8, 0), Padding = new Thickness(0, 0, 8, 0) };
        var left = new DockPanel();
        DockPanel.SetDock(leftHead, Dock.Top);
        DockPanel.SetDock(leftFilter, Dock.Top);
        left.Children.Add(leftHead);
        left.Children.Add(leftFilter);
        left.Children.Add(leftScroll);
        var leftCard = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(12), Background = Ui.Frozen(Color.FromRgb(0x24, 0x24, 0x24)), Child = left };

        var search = Ui.Input();
        Field.SetIcon(search, "Search");
        Field.SetHint(search, $"{Plural(type)} auf Modrinth suchen …");
        search.Margin = new Thickness(0);
        var sort = Ui.Combo(Sorts.Select(s => (object)s.Label));
        sort.SelectedIndex = 0;
        sort.Width = 190;
        sort.Margin = new Thickness(10, 0, 0, 0);
        var tiles = app.Settings.BrowseAsTiles;
        var gridToggle = ViewToggle("Grid", "Kartenansicht", tiles);
        var listToggle = ViewToggle("List", "Listenansicht", !tiles);
        listToggle.Margin = new Thickness(2, 0, 0, 0);
        var viewToggle = new Border
        {
            Style = Ui.Resource<Style>("SegmentGroup"),
            Margin = new Thickness(10, 0, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { gridToggle, listToggle } }
        };
        var searchRow = new DockPanel();
        DockPanel.SetDock(viewToggle, Dock.Right);
        DockPanel.SetDock(sort, Dock.Right);
        searchRow.Children.Add(viewToggle);
        searchRow.Children.Add(sort);
        searchRow.Children.Add(search);

        var chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        Panel results = tiles ? ResultGrid() : new StackPanel();
        var more = new Button { Style = Ui.Resource<Style>("SmallButton"), Content = "Mehr laden", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed };
        var resultStatus = new TextBlock { FontSize = 13, Foreground = Ui.Resource<Brush>("DimText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 40), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var resultStack = new StackPanel { Children = { results, resultStatus, more } };
        void SetTiles(bool value)
        {
            if (tiles == value)
                return;
            tiles = value;
            app.Settings.BrowseAsTiles = value;
            app.SaveSettings();
            var index = resultStack.Children.IndexOf(results);
            resultStack.Children.RemoveAt(index);
            results = value ? ResultGrid() : new StackPanel();
            resultStack.Children.Insert(index, results);
            _ = LoadAsync(reset: true);
        }
        var resultScroll = new ScrollViewer { Content = resultStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, -8, 0), Padding = new Thickness(0, 0, 8, 0) };
        var right = new DockPanel();
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(chips, Dock.Top);
        right.Children.Add(searchRow);
        right.Children.Add(chips);
        right.Children.Add(resultScroll);

        var body = new Grid { Margin = new Thickness(22, 16, 22, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 2);
        body.Children.Add(leftCard);
        body.Children.Add(right);

        var footerText = new TextBlock { FontSize = 12.5, Foreground = Ui.Resource<Brush>("MutedText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var cancel = DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel);
        var done = DialogParts.Primary("Fertig", () => { });
        var footer = DialogParts.Footer(footerText, cancel, done);

        void RenderInstalled()
        {
            leftList.Children.Clear();
            var query = leftFilter.Text.Trim();
            var rows = installed.Select(i => (Name: i.DisplayName, Version: i.Entry?.VersionName ?? i.FileName, Icon: (ImageSource?)i.Icon, Url: (string?)null, Project: (ContentProject?)null))
                .Concat(pending.Values.Select(p => (Name: p.Title, Version: "Wird installiert", Icon: (ImageSource?)null, Url: p.IconUrl, Project: (ContentProject?)p)))
                .Where(r => query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            installedCount.Text = (installed.Count + pending.Count).ToString(CultureInfo.InvariantCulture);
            foreach (var row in rows)
            {
                var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 2), MinHeight = 44 };
                var icon = IconTile(row.Name, row.Icon, row.Url, 32, 8);
                icon.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(icon, Dock.Left);
                dock.Children.Add(icon);
                if (row.Project is { } project)
                {
                    var undo = new Button { Style = Ui.Resource<Style>("TrashButton"), Width = 24, Height = 24, ToolTip = $"{project.Title} doch nicht installieren", Content = new Icon { Kind = "Close", Size = 11 } };
                    undo.Click += (_, _) =>
                    {
                        pending.Remove(project.Id);
                        RenderInstalled();
                        RenderResultStates();
                    };
                    DockPanel.SetDock(undo, Dock.Right);
                    dock.Children.Add(undo);
                    var badge = new Border { Height = 20, Padding = new Thickness(7, 0, 7, 0), CornerRadius = new CornerRadius(10), Background = Ui.Resource<Brush>("Accent"), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "Neu", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } };
                    DockPanel.SetDock(badge, Dock.Right);
                    dock.Children.Add(badge);
                }
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = row.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextPrimary"), TextTrimming = TextTrimming.CharacterEllipsis });
                texts.Children.Add(new TextBlock { Text = row.Version, FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText"), TextTrimming = TextTrimming.CharacterEllipsis });
                dock.Children.Add(texts);
                leftList.Children.Add(new Border { Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(8), Child = dock, Background = row.Project != null ? Ui.Resource<Brush>("AccentFaint") : Brushes.Transparent });
            }
            footerText.Text = pending.Count == 0 ? "Wähle aus, was installiert werden soll."
                : Formats.Count(pending.Count, ContentTypes.Label(type), Plural(type)) + " ausgewählt";
            done.Content = Ui.IconText(pending.Count > 0 ? "Download" : null,
                pending.Count > 0 ? $"{pending.Count} installieren" : "Fertig");
        }

        var buttons = new Dictionary<ContentProject, Button>();

        void RenderResultStates()
        {
            foreach (var (project, button) in buttons)
                ApplyInstallButton(button, project, pending.ContainsKey(project.Id), store.IsInstalled(project.Id));
        }

        void RenderChips()
        {
            chips.Children.Clear();
            foreach (var cat in Categories[type])
            {
                var on = cat == category;
                var chip = new Button
                {
                    Style = Ui.Resource<Style>("SmallButton"),
                    Height = 30,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 6, 6),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Content = cat.Label,
                    Background = on ? Ui.Resource<Brush>("Accent") : Ui.Resource<Brush>("PopupBg"),
                    Foreground = on ? Brushes.White : Ui.Resource<Brush>("TextSecondary")
                };
                Field.SetCorner(chip, new CornerRadius(15));
                chip.Click += (_, _) =>
                {
                    category = cat;
                    RenderChips();
                    _ = LoadAsync(reset: true);
                };
                chips.Children.Add(chip);
            }
        }

        async Task LoadAsync(bool reset)
        {
            var current = ++request;
            if (reset)
            {
                page = 0;
                results.Children.Clear();
                buttons.Clear();
                resultScroll.ScrollToTop();
            }
            more.Visibility = Visibility.Collapsed;
            resultStatus.Text = "Suche auf Modrinth …";
            Ui.Show(resultStatus, true);
            try
            {
                var query = string.Join(" ", new[] { search.Text.Trim(), category.Query ?? "" }.Where(s => s.Length > 0));
                var sortKey = Sorts[sortIndex].Index;
                var found = await app.Modrinth.SearchAsync(query, type, inst, page, 20, category.Facet,
                    sortKey == "name" ? null : sortKey);
                if (current != request)
                    return;
                var items = sortKey == "name"
                    ? found.Items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
                    : found.Items;
                foreach (var project in items)
                    results.Children.Add(tiles ? ResultTile(project) : ResultRow(project));
                RenderResultStates();
                resultStatus.Text = results.Children.Count == 0 ? "Nichts gefunden. Versuch einen anderen Suchbegriff." : "";
                Ui.Show(resultStatus, results.Children.Count == 0);
                Ui.Show(more, (page + 1) * 20 < found.TotalHits);
            }
            catch (Exception ex)
            {
                if (current == request)
                    resultStatus.Text = "Suche fehlgeschlagen: " + ErrorReport.Short(ex);
            }
        }

        Button InstallButton(ContentProject project)
        {
            var install = new Button { Height = 34 };
            buttons[project] = install;
            install.Click += (_, _) =>
            {
                if (working || store.IsInstalled(project.Id))
                    return;
                if (!pending.Remove(project.Id))
                    pending[project.Id] = project;
                RenderInstalled();
                RenderResultStates();
            };
            return install;
        }

        FrameworkElement ResultTile(ContentProject project)
        {
            var icon = IconTile(project.Title, null, project.IconUrl, 56, 13);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            var install = InstallButton(project);
            install.Margin = new Thickness(0, 12, 0, 0);
            var texts = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            texts.Children.Add(new TextBlock { Text = project.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), TextTrimming = TextTrimming.CharacterEllipsis });
            if (project.Author.Length > 0)
                texts.Children.Add(new TextBlock { Text = "von " + project.Author, FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            texts.Children.Add(new TextBlock { Text = project.Description, FontSize = 12, Foreground = Ui.Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD)), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Height = 34, LineHeight = 17, Margin = new Thickness(0, 6, 0, 0) });
            var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            meta.Children.Add(new Icon { Kind = "Download", Size = 12, Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(0, 0, 6, 0) });
            meta.Children.Add(new TextBlock { Text = project.DownloadsText, FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText") });
            texts.Children.Add(meta);
            var dock = new DockPanel();
            DockPanel.SetDock(icon, Dock.Top);
            DockPanel.SetDock(install, Dock.Bottom);
            dock.Children.Add(icon);
            dock.Children.Add(install);
            dock.Children.Add(texts);
            return ResultCard(project, dock, new Thickness(14), new Thickness(0, 0, 8, 8));
        }

        FrameworkElement ResultCard(ContentProject project, UIElement content, Thickness padding, Thickness margin)
        {
            var card = new Border { Padding = padding, CornerRadius = new CornerRadius(12), Background = Ui.Resource<Brush>("InsetBg"), Margin = margin, Child = content, Cursor = Cursors.Hand, ToolTip = "Auf Modrinth ansehen (Doppelklick)" };
            card.MouseEnter += (_, _) => card.Background = Ui.Resource<Brush>("CardHover");
            card.MouseLeave += (_, _) => card.Background = Ui.Resource<Brush>("InsetBg");
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                    Shell.OpenUrl(project.WebsiteUrl);
            };
            return card;
        }

        FrameworkElement ResultRow(ContentProject project)
        {
            var icon = IconTile(project.Title, null, project.IconUrl, 52, 12);
            icon.Margin = new Thickness(0, 0, 14, 0);
            var install = InstallButton(project);
            install.Width = 128;
            install.Margin = new Thickness(12, 0, 0, 0);
            var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
            titleLine.Children.Add(new TextBlock { Text = project.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
            if (project.Author.Length > 0)
                titleLine.Children.Add(new TextBlock { Text = "von " + project.Author, FontSize = 12, Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom });
            var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            meta.Children.Add(new Icon { Kind = "Download", Size = 12, Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(0, 0, 6, 0) });
            meta.Children.Add(new TextBlock { Text = project.DownloadsText + (project.Tags.Length > 0 ? " · " + project.Tags : ""), FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText") });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(titleLine);
            texts.Children.Add(new TextBlock { Text = project.Description, FontSize = 12.5, Foreground = Ui.Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            texts.Children.Add(meta);
            var dock = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(install, Dock.Right);
            dock.Children.Add(icon);
            dock.Children.Add(install);
            dock.Children.Add(texts);
            return ResultCard(project, dock, new Thickness(12, 12, 14, 12), new Thickness(0, 0, 0, 8));
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = LoadAsync(reset: true);
        };
        search.TextChanged += (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        sort.SelectionChanged += (_, _) =>
        {
            sortIndex = Math.Max(0, sort.SelectedIndex);
            _ = LoadAsync(reset: true);
        };
        leftFilter.TextChanged += (_, _) => RenderInstalled();
        more.Click += (_, _) =>
        {
            page++;
            _ = LoadAsync(reset: false);
        };
        done.Click += async (_, _) =>
        {
            if (pending.Count == 0)
            {
                app.Dialogs.ClosePanel();
                return;
            }
            working = true;
            done.IsEnabled = cancel.IsEnabled = false;
            var failed = new List<string>();
            var list = pending.Values.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var project = list[i];
                footerText.Text = $"Installiere {project.Title} ({i + 1} von {list.Count}) …";
                try
                {
                    await store.InstallAsync(project.Id, project.Title, type, new Progress<string>(t => footerText.Text = t));
                    pending.Remove(project.Id);
                }
                catch (Exception ex)
                {
                    failed.Add($"{project.Title}: {ErrorReport.Short(ex)}");
                }
            }
            app.Dialogs.ClosePanel();
            if (failed.Count > 0)
                await app.Dialogs.ShowMessageAsync("Nicht alles konnte installiert werden", string.Join("\n", failed));
        };

        gridToggle.Checked += (_, _) => SetTiles(true);
        listToggle.Checked += (_, _) => SetTiles(false);
        RenderChips();
        RenderInstalled();
        _ = LoadAsync(reset: true);
        _ = store.LoadMissingIconsAsync(installed).ContinueWith(_ => leftList.Dispatcher.InvokeAsync(RenderInstalled));

        var header = DialogParts.Header($"{Plural(type)} hinzufügen", $"Für {Target(inst)} · von Modrinth",
            DialogParts.IconTile("Plus"), app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        var window = Application.Current.MainWindow;
        root.Height = Math.Min(680, (window?.ActualHeight ?? 800) - 60);
        await app.Dialogs.ShowPanelAsync(root, 1120);
        timer.Stop();
    }

    private static RadioButton ViewToggle(string icon, string tip, bool isChecked) => new()
    {
        Style = Ui.Resource<Style>("SegmentIconButton"),
        GroupName = "BrowseView",
        IsChecked = isChecked,
        ToolTip = tip,
        Content = new Icon { Kind = icon, Size = 16 }
    };

    private static UniformGrid ResultGrid() =>
        new() { Columns = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, -8, 0) };

    private static void ApplyInstallButton(Button button, ContentProject project, bool pending, bool installed)
    {
        if (installed)
        {
            button.Style = Ui.Resource<Style>("SmallButton");
            button.Background = Ui.Resource<Brush>("CardHover");
            button.Foreground = Ui.Resource<Brush>("MutedText");
            button.Content = Ui.IconText("Check", "Installiert", 13);
            button.Cursor = Cursors.Arrow;
            button.ToolTip = null;
        }
        else if (pending)
        {
            button.Style = Ui.Resource<Style>("SmallButton");
            button.Background = Ui.Frozen(Color.FromArgb(0x1F, 0xEC, 0x48, 0x99));
            button.BorderBrush = Ui.Frozen(Color.FromArgb(0x66, 0xD6, 0x8C, 0xE6));
            button.BorderThickness = new Thickness(1);
            button.Foreground = Ui.Resource<Brush>("AccentText");
            button.Content = Ui.IconText("Check", "Hinzugefügt", 13);
            button.ToolTip = $"{project.Title} doch nicht installieren";
        }
        else
        {
            button.Style = Ui.Resource<Style>("SmallPrimaryButton");
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.BorderThickness = new Thickness(0);
            button.Content = Ui.IconText("Download", "Installieren", 13);
            button.ToolTip = null;
        }
        button.Height = 34;
        button.FontSize = 12.5;
    }

    public static FrameworkElement IconTile(string name, ImageSource? image, string? url, double size, double radius)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = Letters.BrushFor(name),
            Child = new TextBlock
            {
                Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
                FontSize = size * 0.42,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        ImageSource? source = image;
        if (source == null && url != null)
        {
            try
            {
                source = new BitmapImage(new Uri(url)) { DecodePixelWidth = (int)(size * 2) };
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Symbol laden", ex);
            }
        }
        if (source != null)
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill }
            });
        return grid;
    }

    public static async Task<bool> ChooseVersionAsync(AppServices app, Installation inst, ContentStore store, ContentType type,
        ContentRow row)
    {
        var entry = row.Item.Entry!;
        var changed = false;
        var compatibleOnly = true;
        var radios = new List<(RadioButton Button, ContentVersion Version)>();
        var current = store.CurrentOf(type, entry.ProjectId);
        var list = new StackPanel();
        var status = new TextBlock { FontSize = 13, Foreground = Ui.Resource<Brush>("DimText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        var hint = new TextBlock { FontSize = 12.5, Foreground = Ui.Resource<Brush>("MutedText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var apply = DialogParts.Primary("Version installieren", () => { });
        apply.IsEnabled = false;
        var compat = new CheckBox
        {
            Content = $"Nur passende Versionen ({InstanceText.Version(inst)})",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 12)
        };

        bool IsCurrent(ContentVersion v) => current != null && (v.Id == current.VersionId || v.FileName == current.FileName);

        void UpdateApply()
        {
            var chosen = radios.FirstOrDefault(r => r.Button.IsChecked == true).Version;
            apply.IsEnabled = chosen != null && !IsCurrent(chosen);
            hint.Text = chosen == null ? "" : IsCurrent(chosen) ? "Diese Version ist installiert." : $"Wechselt auf {chosen.Name}.";
        }

        async Task LoadAsync()
        {
            list.Children.Clear();
            radios.Clear();
            status.Text = "Lade Versionen …";
            Ui.Show(status, true);
            try
            {
                var versions = compatibleOnly
                    ? await app.Modrinth.GetVersionsAsync(entry.ProjectId, type, inst)
                    : await app.Modrinth.GetProjectVersionsAsync(entry.ProjectId);
                var newest = ContentVersion.Newest(versions);
                foreach (var version in versions.Take(60))
                {
                    var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    if (!version.IsRelease)
                        tags.Children.Add(Tag(version.ChannelText, Color.FromArgb(0x29, 0xE3, 0xB3, 0x41), Color.FromRgb(0xF0, 0xC9, 0x5A)));
                    if (version == newest)
                        tags.Children.Add(Tag("Neueste", Color.FromArgb(0x29, 0x3F, 0xB9, 0x50), Color.FromRgb(0x6F, 0xDC, 0x80)));
                    if (IsCurrent(version))
                        tags.Children.Add(Tag("Installiert", Color.FromArgb(0x33, 0xEC, 0x48, 0x99), Color.FromRgb(0xEC, 0xC6, 0xF2)));
                    var texts = new StackPanel();
                    texts.Children.Add(new TextBlock { Text = version.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
                    texts.Children.Add(new TextBlock
                    {
                        Text = $"{version.ChannelText} · {version.Date:dd.MM.yyyy}" + (version.Size > 0 ? " · " + Formats.Size(version.Size) : "")
                               + (compatibleOnly ? "" : " · " + string.Join(", ", version.GameVersions.TakeLast(3))),
                        FontSize = 12,
                        Foreground = Ui.Resource<Brush>("MutedText"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    var content = new DockPanel();
                    DockPanel.SetDock(tags, Dock.Right);
                    content.Children.Add(tags);
                    content.Children.Add(texts);
                    var radio = new RadioButton
                    {
                        Style = Ui.Resource<Style>("PickCard"),
                        GroupName = "version-pick",
                        Content = content,
                        IsChecked = IsCurrent(version),
                        Margin = new Thickness(0, 0, 0, 4),
                        Padding = new Thickness(12, 10, 12, 10)
                    };
                    radio.Checked += (_, _) => UpdateApply();
                    radios.Add((radio, version));
                    list.Children.Add(radio);
                }
                status.Text = versions.Count == 0 ? "Keine Version passt zu dieser Instanz." : "";
                Ui.Show(status, versions.Count == 0);
                UpdateApply();
            }
            catch (Exception ex)
            {
                status.Text = "Versionen konnten nicht geladen werden: " + ErrorReport.Short(ex);
            }
        }

        compat.Click += (_, _) =>
        {
            compatibleOnly = compat.IsChecked == true;
            _ = LoadAsync();
        };
        apply.Click += async (_, _) =>
        {
            var chosen = radios.FirstOrDefault(r => r.Button.IsChecked == true).Version;
            if (chosen == null)
                return;
            apply.IsEnabled = false;
            hint.Text = $"Installiere {chosen.Name} …";
            try
            {
                await store.InstallAsync(entry.ProjectId, entry.Title, type, new Progress<string>(t => hint.Text = t),
                    version: chosen, replace: true);
                changed = true;
                app.Dialogs.ClosePanel();
            }
            catch (Exception ex)
            {
                hint.Text = "Fehler: " + ErrorReport.Short(ex);
                apply.IsEnabled = true;
            }
        };

        var scroll = new ScrollViewer { Content = new StackPanel { Children = { list, status } }, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, -8, 0), Padding = new Thickness(0, 0, 8, 0) };
        var body = new StackPanel { Margin = new Thickness(22, 16, 22, 16), Children = { compat, scroll } };
        var header = DialogParts.Header($"Version von {entry.Title}", $"Installiert: {entry.VersionName ?? row.Item.FileName}",
            IconTile(entry.Title, row.Item.Icon, null, 40, 10), app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var footer = DialogParts.Footer(hint, DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), apply);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        _ = LoadAsync();
        await app.Dialogs.ShowPanelAsync(root, 500);
        return changed;
    }

    private static Border Tag(string text, Color background, Color foreground) => new()
    {
        Height = 20,
        Padding = new Thickness(7, 0, 7, 0),
        CornerRadius = new CornerRadius(10),
        Background = Ui.Frozen(background),
        Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Frozen(foreground), VerticalAlignment = VerticalAlignment.Center }
    };

    private static FrameworkElement Working(string text)
    {
        var spinner = Ui.Spinner();
        spinner.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel
        {
            Margin = new Thickness(0, 34, 0, 26),
            Children =
            {
                spinner,
                new TextBlock { Text = text, FontSize = 13, Foreground = Ui.Resource<Brush>("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    private static FrameworkElement AllGood(string title, string text)
    {
        return new StackPanel
        {
            Margin = new Thickness(0, 22, 0, 6),
            Children =
            {
                new Border
                {
                    Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Ui.Frozen(Color.FromArgb(0x29, 0x3F, 0xB9, 0x50)),
                    Child = new Icon { Kind = "Check", Size = 22, Foreground = Ui.Resource<Brush>("Good"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                },
                new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) },
                new TextBlock { Text = text, FontSize = 13, Foreground = Ui.Resource<Brush>("MutedText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    private static (DockPanel Root, ContentControl Body, ContentControl Footer) Frame(AppServices app, string title, string subtitle,
        Border tile)
    {
        var header = DialogParts.Header(title, subtitle, tile, app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var body = new ContentControl { Margin = new Thickness(22, 16, 22, 16), Focusable = false };
        var footer = new ContentControl { Focusable = false };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        return (root, body, footer);
    }

    public static async Task RepairAsync(AppServices app, Installation inst, ContentStore store, ContentType type)
    {
        var tile = DialogParts.IconTile("Wrench");
        tile.Background = Ui.Frozen(Color.FromArgb(0x24, 0xE3, 0xB3, 0x41));
        ((Icon)tile.Child).Foreground = Ui.Frozen(Color.FromRgb(0xF0, 0xC9, 0x5A));
        var title = type switch
        {
            ContentType.Mod => "Mods reparieren",
            ContentType.ResourcePack => "Ressourcepacks prüfen",
            _ => "Shader prüfen"
        };
        var (root, body, footer) = Frame(app, title, $"{Target(inst)}", tile);
        body.Content = Working(type == ContentType.Mod
            ? $"Prüfe, ob alle Mods zu {InstanceText.Version(inst)} und zueinander passen …"
            : "Prüfe Pakete und benötigte Mods …");
        var closeTask = app.Dialogs.ShowPanelAsync(root, 560);

        async Task AnalyzeAsync()
        {
            List<Issue> issues;
            try
            {
                issues = type == ContentType.Mod
                    ? await new ModRepair(store, app.Modrinth).AnalyzeAsync(new Progress<string>())
                    : await new PackRepair(store).AnalyzeAsync(type, new Progress<string>());
            }
            catch (Exception ex)
            {
                body.Content = AllGood("Prüfung fehlgeschlagen", ErrorReport.Short(ex));
                footer.Content = DialogParts.Footer(null, DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel));
                return;
            }

            var problems = issues.Where(i => i.Severity != IssueSeverity.Info || i.CanFix).ToList();
            if (problems.Count == 0)
            {
                body.Content = AllGood("Keine Probleme gefunden", "Alles ist bereit zum Spielen.");
                footer.Content = DialogParts.Footer(null, DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel));
                return;
            }

            var boxes = new List<(CheckBox Box, Issue Issue)>();
            var list = new StackPanel();
            Button? selectedButton = null;
            void UpdateCount()
            {
                var count = boxes.Count(b => b.Box.IsChecked == true);
                if (selectedButton != null)
                {
                    selectedButton.Content = $"Auswahl reparieren ({count})";
                    selectedButton.IsEnabled = count > 0;
                }
            }
            foreach (var issue in problems)
            {
                var (sevText, sevBg, sevFg) = issue.Severity switch
                {
                    IssueSeverity.Error => ("Fehler", Color.FromArgb(0x29, 0xF4, 0x70, 0x67), Color.FromRgb(0xFF, 0x8A, 0x80)),
                    IssueSeverity.Warning => ("Warnung", Color.FromArgb(0x29, 0xE3, 0xB3, 0x41), Color.FromRgb(0xF0, 0xC9, 0x5A)),
                    _ => ("Hinweis", Color.FromArgb(0x29, 0x8A, 0x8A, 0x8A), Color.FromRgb(0xBD, 0xBD, 0xBD))
                };
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new TextBlock { Text = issue.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), TextWrapping = TextWrapping.Wrap, MaxWidth = 340 });
                head.Children.Add(Tag(sevText, sevBg, sevFg));
                var texts = new StackPanel();
                texts.Children.Add(head);
                texts.Children.Add(new TextBlock { Text = issue.Description, FontSize = 12.5, Foreground = Ui.Frozen(Color.FromRgb(0xD0, 0xD0, 0xD0)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 18 });
                if (issue.CanFix)
                {
                    var fix = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    fix.Children.Add(new Icon { Kind = "Wrench", Size = 12, Foreground = Ui.Resource<Brush>("MutedText"), Margin = new Thickness(0, 0, 6, 0) });
                    fix.Children.Add(new TextBlock { Text = issue.FixText ?? "Wird behoben", FontSize = 12, Foreground = Ui.Resource<Brush>("MutedText"), TextWrapping = TextWrapping.Wrap });
                    texts.Children.Add(fix);
                }
                FrameworkElement row;
                if (issue.CanFix)
                {
                    var box = new CheckBox { IsChecked = issue.Selected, Content = texts, VerticalContentAlignment = VerticalAlignment.Top };
                    box.Click += (_, _) => UpdateCount();
                    boxes.Add((box, issue));
                    row = box;
                }
                else
                {
                    row = texts;
                }
                list.Children.Add(new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), Background = Ui.Resource<Brush>("RowBgSoft"), Margin = new Thickness(0, 0, 0, 6), Child = row });
            }
            body.Content = new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, -8, 0), Padding = new Thickness(0, 0, 8, 0) };

            async Task FixAsync(List<Issue> chosen)
            {
                body.Content = Working($"Repariere {Formats.Count(chosen.Count, "Problem", "Probleme")} …");
                footer.Content = null;
                var (_, failed) = await Issue.FixAllAsync(chosen, WorkProgress.For(new Progress<string>()));
                app.Instances.NotifyChanged();
                if (failed.Count > 0)
                {
                    body.Content = AllGood("Nicht alles konnte behoben werden", string.Join("\n", failed));
                    footer.Content = DialogParts.Footer(null, DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel));
                    return;
                }
                body.Content = Working("Prüfe erneut …");
                await AnalyzeAsync();
            }

            selectedButton = DialogParts.Make("SecondButton", "", () => _ = FixAsync(boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Issue).ToList()));
            var all = DialogParts.Primary("Alles reparieren", () => _ = FixAsync(boxes.Select(b => b.Issue).ToList()), "Wrench");
            all.IsEnabled = boxes.Count > 0;
            UpdateCount();
            var cancel = DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel);
            footer.Content = DialogParts.Footer(cancel, selectedButton, all);
        }

        _ = AnalyzeAsync();
        await closeTask;
    }

    public static async Task UpdatesAsync(AppServices app, Installation inst, ContentStore store, ContentType type,
        List<InstalledItem> items)
    {
        var (root, body, footer) = Frame(app, $"Updates für {Plural(type)}", Target(inst), DialogParts.IconTile("Refresh"));
        body.Content = Working($"Vergleiche {Formats.Count(items.Count, ContentTypes.Label(type), Plural(type))} mit Modrinth …");
        var closeTask = app.Dialogs.ShowPanelAsync(root, 560);

        try
        {
            await store.IdentifyUnknownAsync(type);
            await store.CheckUpdatesAsync(items);
        }
        catch (Exception ex)
        {
            body.Content = AllGood("Update-Suche fehlgeschlagen", ErrorReport.Short(ex));
            footer.Content = DialogParts.Footer(null, DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel));
            await closeTask;
            return;
        }

        var updates = items.Where(i => i.HasUpdate).ToList();
        if (updates.Count == 0)
        {
            body.Content = AllGood("Alles ist aktuell", "Für diese Instanz gibt es gerade keine neueren Versionen.");
            footer.Content = DialogParts.Footer(null, DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel));
            await closeTask;
            return;
        }

        var boxes = new List<(CheckBox Box, InstalledItem Item)>();
        var list = new StackPanel();
        Button? selected = null;
        void UpdateCount()
        {
            var count = boxes.Count(b => b.Box.IsChecked == true);
            selected!.Content = $"Auswahl aktualisieren ({count})";
            selected.IsEnabled = count > 0;
        }
        foreach (var item in updates)
        {
            var icon = IconTile(item.DisplayName, item.Icon, null, 38, 9);
            icon.Margin = new Thickness(0, 0, 12, 0);
            var change = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            change.Children.Add(new TextBlock { Text = item.Entry?.VersionName ?? item.FileName, FontSize = 12, Foreground = Ui.Resource<Brush>("MutedText"), MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis });
            change.Children.Add(new Icon { Kind = "ArrowRight", Size = 12, Foreground = Ui.Resource<Brush>("MutedText"), Margin = new Thickness(6, 0, 6, 0) });
            change.Children.Add(new TextBlock { Text = item.Update!.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Frozen(Color.FromRgb(0x6F, 0xDC, 0x80)), MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = item.DisplayName, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
            texts.Children.Add(change);
            texts.Children.Add(new TextBlock { Text = $"{item.Update.ChannelText} vom {item.Update.Date:dd.MM.yyyy}", FontSize = 12, Foreground = Ui.Resource<Brush>("TextSecondary"), Margin = new Thickness(0, 2, 0, 0) });
            var size = new TextBlock { Text = item.Update.Size > 0 ? Formats.Size(item.Update.Size) : "", FontSize = 12, Foreground = Ui.Resource<Brush>("LabelText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var dock = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(size, Dock.Right);
            dock.Children.Add(icon);
            dock.Children.Add(size);
            dock.Children.Add(texts);
            var box = new CheckBox { IsChecked = true, Content = dock, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            box.Click += (_, _) => UpdateCount();
            boxes.Add((box, item));
            list.Children.Add(new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), Background = Ui.Resource<Brush>("RowBgSoft"), Margin = new Thickness(0, 0, 0, 6), Child = box });
        }
        body.Content = new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, -8, 0), Padding = new Thickness(0, 0, 8, 0) };

        async Task ApplyAsync(List<InstalledItem> chosen)
        {
            body.Content = Working($"Lade {Formats.Count(chosen.Count, "Update", "Updates")} herunter …");
            footer.Content = null;
            var failed = new List<string>();
            foreach (var item in chosen)
            {
                try
                {
                    await store.ApplyUpdateAsync(item, new Progress<string>());
                }
                catch (Exception ex)
                {
                    failed.Add($"{item.Entry?.Title ?? item.DisplayName}: {ErrorReport.Short(ex)}");
                }
            }
            body.Content = failed.Count == 0
                ? AllGood("Aktualisiert", Formats.Count(chosen.Count, "Eintrag wurde", "Einträge wurden") + " aktualisiert.")
                : AllGood("Nicht alles konnte aktualisiert werden", string.Join("\n", failed));
            footer.Content = DialogParts.Footer(null, DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel));
        }

        selected = DialogParts.Make("SecondButton", "", () => _ = ApplyAsync(boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Item).ToList()));
        UpdateCount();
        var all = DialogParts.Primary("Alle aktualisieren", () => _ = ApplyAsync(updates), "Download");
        footer.Content = DialogParts.Footer(DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), selected, all);
        await closeTask;
    }
}

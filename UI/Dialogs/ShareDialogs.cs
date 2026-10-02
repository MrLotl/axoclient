using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class ShareDialogs
{
    private const string FailureTitle = "Das hat nicht geklappt";

    private sealed record InstanceChoice(Installation Installation)
    {
        public override string ToString() => $"{Installation.Name}  ·  {Installation.Description}";
    }

    private sealed class FriendPicker
    {
        private readonly List<(FriendInfo Friend, CheckBox Box, Border Row)> _rows = [];
        private readonly TextBox _search;
        private readonly TextBlock _empty;

        public FriendPicker(IReadOnlyList<FriendInfo> friends, string? preselectUuid, Action changed)
        {
            _search = new TextBox { Style = Ui.Resource<Style>("LauncherTextBox"), Margin = new Thickness(0, 0, 0, 10) };
            Field.SetIcon(_search, "Search");
            Field.SetHint(_search, "Freunde suchen …");
            var list = new StackPanel();
            foreach (var friend in friends)
            {
                var head = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Background = Ui.Resource<Brush>("RowBg"),
                    Margin = new Thickness(0, 0, 12, 0),
                    Child = Ui.UrlImage(friend.HeadUrl)
                };
                var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                status.Children.Add(Ui.Dot(friend.Presence));
                status.Children.Add(new TextBlock { Text = friend.PresenceText, FontSize = 12, Foreground = Ui.Resource<Brush>("TextSecondary") });
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = friend.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
                texts.Children.Add(status);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { head, texts } };
                var box = new CheckBox { Content = content, IsChecked = friend.Uuid == preselectUuid, Margin = new Thickness(10, 0, 10, 0) };
                var row = new Border { Height = 52, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Child = box };
                box.Click += (_, _) =>
                {
                    Paint(row, box);
                    changed();
                };
                Paint(row, box);
                _rows.Add((friend, box, row));
                list.Children.Add(row);
            }
            _empty = new TextBlock
            {
                Text = "Kein Freund gefunden.",
                FontSize = 13,
                Foreground = Ui.Resource<Brush>("DimText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 16),
                Visibility = Visibility.Collapsed
            };
            _search.TextChanged += (_, _) =>
            {
                var query = _search.Text.Trim();
                foreach (var (friend, _, row) in _rows)
                    Ui.Show(row, query.Length == 0 || friend.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
                Ui.Show(_empty, _rows.All(r => r.Row.Visibility != Visibility.Visible));
            };
            View = new StackPanel
            {
                Children =
                {
                    _search,
                    new ScrollViewer
                    {
                        Content = new StackPanel { Children = { list, _empty } },
                        MaxHeight = 236,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Margin = new Thickness(0, 0, -8, 0),
                        Padding = new Thickness(0, 0, 8, 0)
                    }
                }
            };
        }

        private static void Paint(Border row, CheckBox box) =>
            row.Background = box.IsChecked == true ? Ui.Resource<Brush>("AccentFaint") : Brushes.Transparent;

        public FrameworkElement View { get; }

        public List<FriendInfo> Selected => _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Friend).ToList();

        public string CountLabel => Selected.Count switch
        {
            0 => "Niemand ausgewählt",
            1 => "1 Freund ausgewählt",
            var n => $"{n} Freunde ausgewählt"
        };

        public string RecipientLabel => Selected.Count == 1 ? Selected[0].Name : $"{Selected.Count} Freunde";
    }

    public static Task ShareInstanceAsync(AppServices app, Installation? inst = null, string? preselectFriend = null) =>
        UiRun.GuardAsync(app, FailureTitle, () => ShareInstanceCoreAsync(app, inst ?? app.Instances.Selected, preselectFriend));

    public static Task ShareContentAsync(AppServices app, ContentPayload payload) =>
        UiRun.GuardAsync(app, FailureTitle, () => SharePayloadAsync(app, payload, $"„{payload.Title}“ senden",
            $"{payload.KindText} · Dein Freund kann es mit einem Klick installieren.", "Cube", ShareKinds.Content, payload.Title));

    public static Task ShareServerAsync(AppServices app, string name, string address)
    {
        var payload = new ServerPayload { Name = name, Address = address };
        return UiRun.GuardAsync(app, FailureTitle, () => SharePayloadAsync(app, payload, $"„{name}“ senden",
            $"{address} · landet in der Serverliste einer Instanz seiner Wahl.", "Server", ShareKinds.Server, payload.Name));
    }

    public static Task<bool> OpenShareAsync(AppServices app, ShareInfo share) =>
        UiRun.GuardAsync(app, FailureTitle, () => OpenShareCoreAsync(app, share));

    public static Task ImportPathAsync(AppServices app, string path) =>
        UiRun.GuardAsync(app, FailureTitle, () => OpenPathAsync(app, path));

    private static async Task<(List<FriendInfo> Friends, string? Problem)> LoadFriendsAsync(AppServices app)
    {
        if (app.Accounts.Session == null)
            return ([], "Melde dich an, um Freunden etwas zu schicken.");
        if (!app.Axo.Available)
            return ([], "Schalte unter Einstellungen „AxoClient-Symbol in der Tabliste“ ein, um Freunden etwas zu schicken.");
        try
        {
            var friends = await app.Axo.GetMutualFriendsAsync();
            return (friends, friends.Count == 0
                ? "Du hast noch keine Freunde, die dich ebenfalls hinzugefügt haben. Nur mit ihnen kannst du etwas teilen."
                : null);
        }
        catch (Exception ex)
        {
            return ([], "Die Freunde konnten nicht geladen werden: " + ErrorReport.Short(ex));
        }
    }

    private static FrameworkElement SentView(string title)
    {
        var circle = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(26),
            Background = Ui.Resource<Brush>("Accent"),
            Effect = Ui.Resource<System.Windows.Media.Effects.Effect>("AccentShadow"),
            Child = new Icon { Kind = "Check", Size = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        return new StackPanel
        {
            Margin = new Thickness(0, 26, 0, 14),
            Children =
            {
                circle,
                new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) },
                new TextBlock { Text = "Deine Freunde bekommen eine Benachrichtigung im Launcher.", FontSize = 13, Foreground = Ui.Resource<Brush>("MutedText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) }
            }
        };
    }

    private static (DockPanel Root, ContentControl Body, ContentControl Footer, TextBlock Subtitle) Frame(AppServices app,
        string title, string subtitle, FrameworkElement leading)
    {
        var header = DialogParts.Header(title, subtitle, leading, app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var sub = header.Children.OfType<StackPanel>().Last().Children.OfType<TextBlock>().Last();
        var body = new ContentControl { Margin = new Thickness(22, 16, 22, 16), Focusable = false };
        var footer = new ContentControl { Focusable = false };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        return (root, body, footer, sub);
    }

    private static FrameworkElement InstanceThumb(Installation inst)
    {
        var image = InstanceIcons.Load(inst);
        return new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Top,
            Background = image != null ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : InstanceText.Placeholder(inst)
        };
    }

    private sealed record ShareOption(string Key, CheckBox Box, Border Row);

    private static async Task ShareInstanceCoreAsync(AppServices app, Installation? inst, string? preselectFriend)
    {
        if (inst == null)
            return;
        var (friends, problem) = await LoadFriendsAsync(app);
        var store = app.ContentOf(inst);
        var mods = inst.CanUseMods ? store.CountFiles(ContentType.Mod) : 0;
        var packs = store.CountFiles(ContentType.ResourcePack);
        var shaders = store.CountFiles(ContentType.Shader);
        var servers = new ServerStore(inst.GameDir).Load().Count;
        var hasOptions = File.Exists(inst.OptionsFile);
        var (root, body, footer, subtitle) = Frame(app, $"„{inst.Name}“ senden", "Wähle aus, wer die Instanz bekommen soll.",
            InstanceThumb(inst));

        FriendPicker? picker = null;
        Button? next = null;
        TextBlock? count = null;

        var options = new List<ShareOption>();
        void AddOption(string key, string label, string detail, bool available, bool on)
        {
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") });
            texts.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = Ui.Resource<Brush>("MutedText"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            var box = new CheckBox { Content = texts, IsChecked = available && on, IsEnabled = available, Margin = new Thickness(12, 0, 12, 0) };
            var row = new Border { Height = 62, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Child = box };
            options.Add(new ShareOption(key, box, row));
        }
        AddOption("mods", "Mods", Formats.Count(mods, "Mod", "Mods") + " mit Versionen", mods > 0, true);
        AddOption("packs", "Ressourcepacks", Formats.Count(packs, "Ressourcepack", "Ressourcepacks"), packs > 0, true);
        AddOption("shaders", "Shader", Formats.Count(shaders, "Shader", "Shader"), shaders > 0, true);
        AddOption("servers", "Server", Formats.Count(servers, "Server", "Server") + " aus der Liste", servers > 0, true);
        AddOption("settings", "Einstellungen", "Version, Loader, RAM, Java-Argumente", false, true);
        options[^1].Box.IsChecked = true;
        AddOption("ingame", "Ingame-Einstellungen", hasOptions ? "Steuerung, Grafik, Sound (options.txt)" : "Noch keine gespeichert",
            hasOptions, false);

        void Paint()
        {
            foreach (var option in options)
                option.Row.Background = option.Box.IsChecked == true ? Ui.Resource<Brush>("AccentFaint") : Ui.Resource<Brush>("RowBgSoft");
        }

        ExportOptions Chosen()
        {
            bool On(string key) => options.Any(o => o.Key == key && o.Box.IsChecked == true);
            return new ExportOptions(On("mods"), On("packs"), On("shaders"), On("servers"), On("ingame"), false);
        }

        FrameworkElement Steps(int step)
        {
            StackPanel Step(int number, string label)
            {
                var on = number <= step;
                var dot = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Background = on ? Ui.Resource<Brush>("Accent") : Ui.Resource<Brush>("ChipBg"),
                    Margin = new Thickness(0, 0, 7, 0),
                    Child = new TextBlock
                    {
                        Text = number.ToString(),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = on ? Brushes.White : Ui.Resource<Brush>("MutedText"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                var text = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = number == step ? Ui.Resource<Brush>("TextStrong")
                        : number < step ? Ui.Resource<Brush>("TextSecondary") : Ui.Resource<Brush>("DimText")
                };
                return new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } };
            }
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 14),
                Children =
                {
                    Step(1, "Freunde"),
                    new Border { Width = 24, Height = 1, Background = Ui.Resource<Brush>("FieldBorder"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                    Step(2, "Inhalte")
                }
            };
        }

        void ShowStep1()
        {
            subtitle.Text = "Wähle aus, wer die Instanz bekommen soll.";
            var content = new StackPanel { Children = { Steps(1) } };
            if (picker == null)
                content.Children.Add(new TextBlock { Text = problem ?? "", Style = Ui.Resource<Style>("Body"), Margin = new Thickness(0, 0, 0, 8) });
            else
            {
                if (picker.View.Parent is Panel parent)
                    parent.Children.Remove(picker.View);
                content.Children.Add(picker.View);
            }
            var file = new Button { Style = Ui.Resource<Style>("LinkButton"), Content = "Stattdessen als Datei speichern …", Margin = new Thickness(0, 12, 0, 0) };
            file.Click += (_, _) => ShowStep2(toFile: true);
            content.Children.Add(file);
            body.Content = content;
            count = new TextBlock { Text = picker?.CountLabel ?? "", FontSize = 12.5, Foreground = Ui.Resource<Brush>("MutedText"), VerticalAlignment = VerticalAlignment.Center };
            next = DialogParts.Primary("Weiter", () => ShowStep2(toFile: false), "ArrowRight");
            next.IsEnabled = picker?.Selected.Count > 0;
            footer.Content = DialogParts.Footer(count, DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), next);
        }

        void ShowStep2(bool toFile)
        {
            var recipients = toFile || picker == null ? [] : picker.Selected;
            subtitle.Text = toFile ? $"Als Datei · {InstanceText.Version(inst)}" : $"Für {picker!.RecipientLabel} · {InstanceText.Version(inst)}";
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, 0) };
            foreach (var option in options)
            {
                if (option.Row.Parent is Panel parent)
                    parent.Children.Remove(option.Row);
                grid.Children.Add(option.Row);
            }
            var allNone = new Button { Style = Ui.Resource<Style>("LinkButton"), HorizontalAlignment = HorizontalAlignment.Right };
            void UpdateAll()
            {
                allNone.Content = options.Where(o => o.Box.IsEnabled).All(o => o.Box.IsChecked == true) ? "Keine auswählen" : "Alle auswählen";
                Paint();
            }
            allNone.Click += (_, _) =>
            {
                var target = !options.Where(o => o.Box.IsEnabled).All(o => o.Box.IsChecked == true);
                foreach (var option in options.Where(o => o.Box.IsEnabled))
                    option.Box.IsChecked = target;
                UpdateAll();
            };
            foreach (var option in options)
            {
                option.Box.Click -= OptionClicked;
                option.Box.Click += OptionClicked;
            }
            void OptionClicked(object? s, RoutedEventArgs e) => UpdateAll();
            UpdateAll();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(allNone, Dock.Right);
            head.Children.Add(allNone);
            head.Children.Add(new TextBlock { Text = "Was soll übertragen werden?", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var info = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = Ui.Frozen(Color.FromRgb(0x24, 0x24, 0x24)),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new DockPanel
                {
                    Children =
                    {
                        new Icon { Kind = "Info", Size = 15, Foreground = Ui.Resource<Brush>("TextSecondary"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top },
                        new TextBlock
                        {
                            Text = "Es werden nur Namen und Versionen übertragen – der Launcher deines Freundes lädt die offiziellen Dateien selbst herunter. Welten und Screenshots bleiben bei dir.",
                            FontSize = 12.5,
                            Foreground = Ui.Resource<Brush>("TextSecondary"),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            body.Content = new StackPanel { Children = { Steps(2), head, grid, info } };
            var back = DialogParts.Secondary("Zurück", ShowStep1);
            var send = DialogParts.Primary(toFile ? "Datei speichern" : $"An {picker!.RecipientLabel} senden", () => { },
                toFile ? "Download" : "Send");
            send.Click += async (_, _) =>
            {
                if (toFile)
                {
                    await SaveFileAsync(app, inst, Chosen());
                    return;
                }
                send.IsEnabled = back.IsEnabled = false;
                try
                {
                    await BuildAndDeliverAsync(app, inst, Chosen(), recipients, null,
                        new WorkProgress(new Progress<string>(), new Progress<double>(), CancellationToken.None));
                    body.Content = SentView($"An {picker!.RecipientLabel} gesendet");
                    footer.Content = null;
                    await Task.Delay(1600);
                    app.Dialogs.ClosePanel();
                }
                catch (Exception ex)
                {
                    send.IsEnabled = back.IsEnabled = true;
                    await app.Dialogs.ShowErrorAsync("Senden fehlgeschlagen", ex);
                }
            };
            footer.Content = DialogParts.Footer(back, send);
        }

        if (friends.Count > 0)
            picker = new FriendPicker(friends, preselectFriend, () =>
            {
                if (count != null)
                    count.Text = picker!.CountLabel;
                if (next != null)
                    next.IsEnabled = picker!.Selected.Count > 0;
            });
        ShowStep1();
        await app.Dialogs.ShowPanelAsync(root, 500);
    }

    private static async Task SaveFileAsync(AppServices app, Installation inst, ExportOptions options)
    {
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Instanz als Datei speichern",
            Filter = "AxoClient-Paket (*.json)|*.json",
            FileName = Sanitize.FileName(inst.Name, "Instanz") + ".axoclient.json"
        };
        if (save.ShowDialog(Application.Current.MainWindow) != true)
            return;
        app.Dialogs.ClosePanel();
        var report = await UiRun.RunAsync(app, "Paket wird vorbereitet",
            progress => BuildAndDeliverAsync(app, inst, options, [], save.FileName, progress), "Speichern fehlgeschlagen");
        if (report != null)
            await UiRun.ShowReportAsync(app, "Gespeichert", report);
    }

    private static async Task<List<string>> BuildAndDeliverAsync(AppServices app, Installation inst, ExportOptions options,
        List<FriendInfo> recipients, string? filePath, WorkProgress progress)
    {
        var report = new List<string>();
        var result = await new InstanceExporter(app).BuildAsync(inst, options, progress);
        var json = ShareJson.Write(result.Manifest);
        report.Add($"Instanz „{inst.Name}“ ({InstanceText.Version(inst)}). {result.Manifest.Describe()}");
        report.AddRange(result.Notes);

        if (json.Length > ShareValidation.MaxJsonChars)
            throw new InvalidOperationException("Das Paket ist zu groß zum Teilen. Wähle weniger aus, z.B. ohne Ingame-Einstellungen.");

        if (filePath != null)
        {
            await File.WriteAllTextAsync(filePath, json);
            report.Add($"Als Datei gespeichert:\n{filePath}");
        }
        if (recipients.Count > 0)
        {
            var failed = await SendAsync(app, recipients, ShareKinds.Instance, inst.Name, json);
            if (failed.Count > 0)
                throw new InvalidOperationException("Nicht zugestellt:\n" + string.Join("\n", failed));
        }
        return report;
    }

    private static async Task SharePayloadAsync(AppServices app, SharePayload payload, string title, string subtitle,
        string icon, string kind, string shareTitle)
    {
        string json;
        try
        {
            payload.Validate();
            json = ShareJson.Write(payload);
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowErrorAsync("Teilen nicht möglich", ex);
            return;
        }

        var (friends, problem) = await LoadFriendsAsync(app);
        if (friends.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync("Senden nicht möglich", problem ?? "Es gibt niemanden, dem du etwas schicken kannst.");
            return;
        }

        var (root, body, footer, _) = Frame(app, title, subtitle, DialogParts.IconTile(icon));
        var count = new TextBlock { FontSize = 12.5, Foreground = Ui.Resource<Brush>("MutedText"), VerticalAlignment = VerticalAlignment.Center };
        var send = DialogParts.Primary("Senden", () => { }, "Send");
        send.IsEnabled = false;
        FriendPicker? picker = null;
        picker = new FriendPicker(friends, null, () =>
        {
            count.Text = picker!.CountLabel;
            send.IsEnabled = picker.Selected.Count > 0;
        });
        count.Text = picker.CountLabel;
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            var failed = await SendAsync(app, picker.Selected, kind, shareTitle, json);
            if (failed.Count > 0)
            {
                send.IsEnabled = true;
                await app.Dialogs.ShowMessageAsync("Nicht zugestellt", string.Join("\n", failed));
                return;
            }
            body.Content = SentView($"An {picker.RecipientLabel} gesendet");
            footer.Content = null;
            await Task.Delay(1600);
            app.Dialogs.ClosePanel();
        };
        body.Content = picker.View;
        footer.Content = DialogParts.Footer(count, DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), send);
        await app.Dialogs.ShowPanelAsync(root, 440);
    }

    private static async Task<List<string>> SendAsync(AppServices app, List<FriendInfo> recipients, string kind,
        string title, string json)
    {
        var failed = new List<string>();
        foreach (var friend in recipients)
        {
            try
            {
                await app.Axo.SendShareAsync(friend.Uuid, kind, title, json);
            }
            catch (Exception ex)
            {
                failed.Add($"{friend.Name}: {ErrorReport.Short(ex)}");
            }
        }
        return failed;
    }

    private static async Task<bool> OpenShareCoreAsync(AppServices app, ShareInfo share)
    {
        var json = await UiRun.RunAsync(app, "Paket wird geholt",
            _ => app.Axo.GetSharePayloadAsync(share.Id), "Das Paket konnte nicht geöffnet werden");
        if (json == null || !await HandlePayloadAsync(app, share.Kind, json, $"von {share.FromName}"))
            return false;
        try
        {
            await app.Axo.DeleteShareAsync(share.Id);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Geteiltes Paket aus dem Postfach entfernen", ex);
        }
        return true;
    }

    private static async Task OpenPathAsync(AppServices app, string path)
    {
        if (path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            await ModpackDialogs.InstallFileAsync(app, path);
            return;
        }

        string text;
        try
        {
            if (new FileInfo(path).Length > ShareValidation.MaxFileBytes)
                throw new ShareFormatException("Die Datei ist zu groß für ein AxoClient-Paket.");
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is ShareFormatException or IOException)
        {
            await app.Dialogs.ShowErrorAsync("Datei nicht lesbar", ex);
            return;
        }

        if (ShareJson.DetectKind(text) is not { } kind)
        {
            await app.Dialogs.ShowMessageAsync("Keine AxoClient-Datei",
                "Diese Datei ist kein AxoClient-Paket. Modpacks von Modrinth haben die Endung .mrpack.");
            return;
        }
        await HandlePayloadAsync(app, kind, text, "aus einer Datei");
    }

    private static async Task<bool> HandlePayloadAsync(AppServices app, string kind, string json, string source)
    {
        try
        {
            return kind switch
            {
                ShareKinds.Instance => await ReceiveInstanceAsync(app, ShareJson.Read<InstanceManifest>(json, kind), source),
                ShareKinds.Overlay => await ReceiveOverlayAsync(app, ShareJson.Read<OverlayPayload>(json, kind), source),
                ShareKinds.Content => await ReceiveContentAsync(app, ShareJson.Read<ContentPayload>(json, kind), source),
                ShareKinds.Server => await ReceiveServerAsync(app, ShareJson.Read<ServerPayload>(json, kind), source),
                _ => await UnknownAsync(app)
            };
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowErrorAsync("Paket ungültig", ex);
            return false;
        }
    }

    private static async Task<bool> UnknownAsync(AppServices app)
    {
        await app.Dialogs.ShowMessageAsync("Unbekanntes Paket",
            "Dieses Paket stammt vermutlich aus einer neueren AxoClient-Version. Bitte aktualisiere den Launcher.");
        return false;
    }

    private static async Task<bool> ReceiveInstanceAsync(AppServices app, InstanceManifest manifest, string source)
    {
        var importer = new InstanceImporter(app);
        var checkedManifest = await UiRun.RunAsync(app, "Paket wird geprüft", async progress =>
        {
            progress.Text.Report("Frage Modrinth nach den Namen der Inhalte...");
            await importer.ResolveTitlesAsync(manifest);
            return manifest;
        }, "Das Paket konnte nicht geprüft werden");
        if (checkedManifest == null)
            return false;

        var nameBox = Ui.Input(manifest.Name.Length > 0 ? manifest.Name : "Importierte Instanz");
        var names = string.Join(", ", manifest.Content.Take(10).Select(c => c.Title));
        var servers = manifest.Servers.Count > 0 ? Ui.Check($"Server übernehmen ({manifest.Servers.Count})") : null;
        var options = manifest.Options != null ? Ui.Check("Einstellungen & Tastenbelegung übernehmen") : null;
        var overlay = manifest.Overlay != null ? Ui.Check("Overlay-Einstellungen übernehmen") : null;
        var form = Ui.Stack(
            Ui.Note($"Eine Instanz {source}. Sie wird neu angelegt, deine anderen Instanzen bleiben unberührt."),
            Ui.Label("Name der Instanz"), nameBox,
            Ui.Note($"{manifest.Loader} · Minecraft {manifest.Minecraft}\n{manifest.Describe()}", 6),
            names.Length > 0 ? Ui.Note("Enthält u. a.: " + names + (manifest.Content.Count > 10 ? ", ..." : ""), 10, 11) : null,
            servers, options, overlay,
            Ui.Note("Alle Mods, Ressourcenpakete und Shader lädt AxoClient von Modrinth.", 0, 11));

        if (!await app.Dialogs.ShowFormAsync("Instanz übernehmen", form, "Installieren", () => nameBox.Text.Trim().Length > 0))
            return false;

        var choices = new ImportChoices(nameBox.Text.Trim(), Ui.IsChosen(servers), Ui.IsChosen(options), Ui.IsChosen(overlay));
        var result = await UiRun.RunAsync(app, "Instanz wird eingerichtet",
            progress => importer.ImportAsync(manifest, choices, progress), "Import fehlgeschlagen");
        if (result == null)
            return false;

        await UiRun.ShowReportAsync(app, "Instanz übernommen", result.Report);
        return true;
    }

    private static async Task<bool> ReceiveOverlayAsync(AppServices app, OverlayPayload payload, string source)
    {
        var what = payload.ProfileName.Length > 0 ? $"Overlay-Profil \"{payload.ProfileName}\"" : "Overlay-Einstellungen";
        var target = await ChooseInstanceAsync(app, "Overlay übernehmen",
            $"{what} {source}" + (payload.Name.Length > 0 ? $" (aus \"{payload.Name}\")" : "") +
            $": {OverlayProfile.CountEnabled(payload.Config)} Anzeigen eingeschaltet.\n\nIn welche Instanz sollen sie übernommen werden?",
            _ => true, preferred: i => BadgeMod.IsActiveFor(i, app.Settings), confirmText: "Weiter");
        if (target == null)
            return false;

        var overlays = new OverlayStore(target);
        var existing = overlays.ProfileNames();
        var nameBox = Ui.Input(payload.ProfileName.Length > 0 ? payload.ProfileName
            : payload.Name.Length > 0 ? OverlayStore.CleanProfileName("Overlay " + payload.Name) : "Overlay");
        var asProfile = Ui.Check("Als Profil speichern");
        var activate = Ui.Check("Sofort aktivieren (ersetzt die aktuellen Einstellungen, Sicherung als .bak)", false);
        var note = Ui.Note("", 6, 11);
        var form = Ui.Stack(asProfile, Ui.Label("Name des Profils"), nameBox, activate, note);

        bool Validate()
        {
            var name = OverlayStore.CleanProfileName(nameBox.Text);
            nameBox.IsEnabled = asProfile.IsChecked == true;
            note.Text = asProfile.IsChecked == true && existing.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? $"Ein Profil \"{name}\" gibt es schon; es wird ersetzt."
                : "";
            return activate.IsChecked == true || (asProfile.IsChecked == true && name.Length > 0);
        }

        nameBox.TextChanged += (_, _) => Validate();
        asProfile.Click += (_, _) => Validate();
        activate.Click += (_, _) => Validate();
        Validate();
        if (!await app.Dialogs.ShowFormAsync("Overlay übernehmen", form, "Übernehmen", Validate))
            return false;

        var doActivate = activate.IsChecked == true;
        if (doActivate && app.Games.IsRunning(target))
        {
            await app.Dialogs.ShowMessageAsync("Minecraft läuft noch",
                $"\"{target.Name}\" läuft gerade und würde die Einstellungen beim Schließen wieder überschreiben. " +
                "Beende das Spiel und öffne das Paket danach erneut, oder speichere es nur als Profil.");
            return false;
        }

        var received = OverlayProfile.From(payload.Config).Detached();
        string? savedAs = null;
        try
        {
            if (asProfile.IsChecked == true)
                savedAs = overlays.WriteProfile(nameBox.Text, received);
            if (doActivate)
                overlays.WriteActive(received.ToElement());
        }
        catch (IOException ex)
        {
            await app.Dialogs.ShowErrorAsync("Übernehmen fehlgeschlagen", ex);
            return false;
        }

        var lines = new List<string>();
        if (savedAs != null)
            lines.Add($"Profil \"{savedAs}\" gespeichert. Im Spiel (rechte Umschalttaste → Profile) lässt es sich laden.");
        if (doActivate)
            lines.Add($"Die Einstellungen gelten jetzt in \"{target.Name}\".");
        if (!BadgeMod.IsActiveFor(target, app.Settings))
            lines.Add("Achtung: Das Overlay gibt es nur mit Fabric und einer Minecraft-Version, für die AxoClient die Mod " +
                      "mitbringt. In dieser Instanz bleibt es zunächst ohne Wirkung.");
        await UiRun.ShowReportAsync(app, "Overlay übernommen", lines);
        return true;
    }

    private static async Task<bool> ReceiveContentAsync(AppServices app, ContentPayload payload, string source)
    {
        if (payload.InstallType is not { } type)
            return await ModpackDialogs.InstallProjectAsync(app, payload.ProjectId, payload.Title, payload.VersionId);

        var project = await UiRun.RunAsync(app, "Inhalt wird gesucht", async progress =>
        {
            progress.Text.Report("Frage Modrinth...");
            var (id, title) = await app.Modrinth.GetProjectInfoAsync(payload.ProjectId);
            return new ProjectSummary(id, title, null);
        }, "Auf Modrinth nicht gefunden");
        if (project == null)
            return false;

        var target = await ChooseInstanceAsync(app, $"{payload.KindText} installieren",
            $"\"{project.Title}\" ({payload.KindText}) {source}.\n\nIn welche Instanz soll es installiert werden?",
            i => type != ContentType.Mod || i.CanUseMods, preferred: null, confirmText: "Installieren");
        if (target == null)
            return false;

        var store = app.ContentOf(target);
        if (store.IsInstalled(project.Id))
        {
            await app.Dialogs.ShowMessageAsync("Schon installiert", $"\"{project.Title}\" ist in \"{target.Name}\" schon vorhanden.");
            return true;
        }

        var installed = await UiRun.RunAsync(app, "Wird installiert", async progress =>
        {
            var version = payload.VersionId != null
                          && await app.Modrinth.GetVersionAsync(payload.VersionId) is { } pinned
                          && pinned.ProjectId == project.Id && pinned.Supports(target, type)
                ? pinned
                : null;
            await store.InstallAsync(project.Id, project.Title, type, progress.Text, version: version);
            return project.Title;
        }, "Installation fehlgeschlagen");
        if (installed == null)
            return false;

        await app.Dialogs.ShowMessageAsync("Installiert", $"\"{installed}\" wurde in \"{target.Name}\" installiert.");
        return true;
    }

    private static async Task<bool> ReceiveServerAsync(AppServices app, ServerPayload payload, string source)
    {
        var target = await ChooseInstanceAsync(app, "Server übernehmen",
            $"Server \"{payload.Name}\" ({payload.Address}) {source}.\n\nIn die Serverliste welcher Instanz soll er aufgenommen werden?",
            _ => true, preferred: null, confirmText: "Übernehmen");
        if (target == null)
            return false;

        try
        {
            var added = new ServerStore(target.GameDir).Merge([ServerStore.Create(payload.Name, payload.Address)]);
            await app.Dialogs.ShowMessageAsync(added > 0 ? "Server übernommen" : "Schon vorhanden",
                added > 0
                    ? $"\"{payload.Name}\" steht jetzt in der Serverliste von \"{target.Name}\"."
                    : $"Ein Server mit dieser Adresse steht in \"{target.Name}\" schon in der Liste.");
            return true;
        }
        catch (IOException ex)
        {
            await app.Dialogs.ShowErrorAsync("Übernehmen fehlgeschlagen", ex);
            return false;
        }
    }

    private static async Task<Installation?> ChooseInstanceAsync(AppServices app, string title, string intro,
        Func<Installation, bool> filter, Func<Installation, bool>? preferred, string confirmText)
    {
        var eligible = app.Instances.All.Where(filter).Select(i => new InstanceChoice(i)).ToList();
        if (eligible.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync(title,
                "Es gibt keine passende Instanz dafür (z.B. sind Mods in Vanilla-Instanzen nicht möglich). " +
                "Lege zuerst eine Instanz mit Fabric oder Forge an.");
            return null;
        }

        var box = Ui.Combo(eligible);
        box.SelectedItem = (preferred == null ? null : eligible.FirstOrDefault(c => preferred(c.Installation)))
                           ?? eligible.FirstOrDefault(c => c.Installation == app.Instances.Selected)
                           ?? eligible[0];
        var form = Ui.Stack(Ui.Note(intro), Ui.Label("Instanz"), box);
        return await app.Dialogs.ShowFormAsync(title, form, confirmText, () => box.SelectedItem != null)
            ? ((InstanceChoice)box.SelectedItem!).Installation
            : null;
    }
}

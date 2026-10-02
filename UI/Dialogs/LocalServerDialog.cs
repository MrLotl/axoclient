using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class LocalServerDialog
{
    public static async Task<LocalServer?> ShowAsync(AppServices app, LocalServer? existing)
    {
        var host = app.LocalServers;
        var create = existing == null;
        LocalServer? result = null;
        byte[]? icon = null;
        var resetIcon = false;
        var software = existing?.Software ?? ServerSoftware.Paper;
        var versionRequest = 0;

        var iconPreview = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0), Effect = Ui.Resource<System.Windows.Media.Effects.Effect>("SoftShadow") };
        void ShowIcon()
        {
            var image = icon != null ? Images.FromBytes(icon)
                : !resetIcon && existing != null && File.Exists(existing.IconFile) ? Images.FromFile(existing.IconFile) : null;
            iconPreview.Background = image != null
                ? new ImageBrush(image) { Stretch = Stretch.UniformToFill }
                : Letters.BrushFor(existing?.Name ?? "Server");
        }
        var change = DialogParts.Make("SmallButton", "Ändern", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = InstanceIcons.FileFilter, Title = "Server-Bild wählen" };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true)
                return;
            icon = File.ReadAllBytes(dialog.FileName);
            resetIcon = false;
            ShowIcon();
        }, "Image");
        var reset = new Button { Style = Ui.Resource<Style>("SmallButton"), Width = 32, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Server-Bild zurücksetzen", Content = new Icon { Kind = "Reset", Size = 14 } };
        reset.Click += (_, _) =>
        {
            icon = null;
            resetIcon = true;
            ShowIcon();
        };
        var iconTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        iconTexts.Children.Add(new TextBlock { Text = "Server-Bild", FontSize = 13, FontWeight = FontWeights.SemiBold });
        iconTexts.Children.Add(new TextBlock { Text = "Wird auch als Server-Icon (64 × 64) im Spiel angezeigt", FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(0, 3, 0, 0) });
        var iconRow = new DockPanel();
        DockPanel.SetDock(iconPreview, Dock.Left);
        DockPanel.SetDock(reset, Dock.Right);
        DockPanel.SetDock(change, Dock.Right);
        iconRow.Children.Add(iconPreview);
        iconRow.Children.Add(reset);
        iconRow.Children.Add(change);
        iconRow.Children.Add(iconTexts);
        var iconBox = new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), Background = Ui.Frozen(Color.FromRgb(0x23, 0x23, 0x23)), Child = iconRow, Margin = new Thickness(0, 0, 0, 16) };
        ShowIcon();

        var name = Ui.Input(existing?.Name ?? "");
        Field.SetHint(name, "z. B. Survival mit Freunden");
        name.Margin = new Thickness(0, 0, 0, 16);

        var group = Guid.NewGuid().ToString("N");
        var softwareGrid = new UniformGrid { Rows = 1 };
        var softwareHint = new TextBlock { FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(0, 7, 0, 16) };
        var version = Ui.Combo([]);
        version.Margin = new Thickness(0);
        var port = Ui.Input((existing?.Port ?? host.NextPort()).ToString());
        port.Margin = new Thickness(0);
        var maxPlayers = Ui.Input((existing?.MaxPlayers ?? 10).ToString());
        maxPlayers.Margin = new Thickness(0);

        var eula = new CheckBox { Content = new TextBlock { Inlines = { new System.Windows.Documents.Run("Ich akzeptiere die "), new System.Windows.Documents.Run("Minecraft-EULA") { Foreground = Ui.Resource<Brush>("AccentText"), FontWeight = FontWeights.SemiBold } } } };
        var submit = DialogParts.Primary(create ? "Server erstellen" : "Speichern", () => { });
        async Task LoadVersionsAsync()
        {
            var request = ++versionRequest;
            softwareHint.Text = LocalServer.SoftwareHint(software);
            version.ItemsSource = new[] { "Lade Versionen …" };
            version.SelectedIndex = 0;
            version.IsEnabled = false;
            try
            {
                var versions = await ServerDownloads.VersionsAsync(app.Http, software);
                if (request != versionRequest)
                    return;
                version.ItemsSource = versions.Take(80).ToList();
                var wanted = existing?.Software == software ? existing.Version : versions.FirstOrDefault();
                version.SelectedItem = versions.Contains(wanted ?? "") ? wanted : versions.FirstOrDefault();
                version.IsEnabled = true;
            }
            catch (Exception ex)
            {
                if (request != versionRequest)
                    return;
                version.ItemsSource = existing?.Software == software && existing.Version.Length > 0 ? new[] { existing.Version } : Array.Empty<string>();
                version.SelectedIndex = 0;
                softwareHint.Text = "Versionen konnten nicht geladen werden: " + ErrorReport.Short(ex);
            }
            Validate();
        }

        foreach (var option in Enum.GetValues<ServerSoftware>())
        {
            var button = new RadioButton
            {
                Content = option.ToString(),
                GroupName = group,
                Style = Ui.Resource<Style>("SegmentButton"),
                Height = 32,
                Padding = new Thickness(6, 0, 6, 0),
                IsChecked = option == software,
                IsEnabled = existing?.Imported != true
            };
            button.Checked += (_, _) =>
            {
                software = option;
                _ = LoadVersionsAsync();
            };
            softwareGrid.Children.Add(button);
        }

        var ramValue = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Resource<Brush>("TextStrong") };
        var ram = new Slider { Minimum = 1, Maximum = 16, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Math.Clamp((existing?.RamMb ?? 4096) / 1024, 1, 16), Margin = new Thickness(0, 6, 0, 0) };
        ram.ValueChanged += (_, _) => ramValue.Text = $"{(int)ram.Value} GB";
        ramValue.Text = $"{(int)ram.Value} GB";
        var ramHead = new DockPanel();
        DockPanel.SetDock(ramValue, Dock.Right);
        ramHead.Children.Add(ramValue);
        ramHead.Children.Add(new TextBlock { Text = "Arbeitsspeicher", Style = Ui.Resource<Style>("FieldLabel"), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Bottom });

        var openSwitch = new CheckBox { Style = Ui.Resource<Style>("ToggleSwitch"), IsChecked = existing?.OpenToInternet ?? false, Margin = new Thickness(16, 0, 0, 0) };
        DockPanel.SetDock(openSwitch, Dock.Right);
        var openTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        openTexts.Children.Add(new TextBlock { Text = "Für Freunde im Internet öffnen", FontSize = 13.5, FontWeight = FontWeights.Medium, Foreground = Ui.Resource<Brush>("TextPrimary") });
        openTexts.Children.Add(new TextBlock
        {
            Text = "Gibt den Port beim Start per UPnP im Router frei, damit Freunde über „Beitreten“ mitspielen können. " +
                   "Der Server ist dann aus dem Internet erreichbar – nutze eine Whitelist, wenn nicht jeder rein soll.",
            FontSize = 12,
            Foreground = Ui.Resource<Brush>("MutedText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        var openRow = new DockPanel { Margin = new Thickness(0, 16, 0, 0), Children = { openSwitch, openTexts } };

        var eulaBox = new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(9), Background = Ui.Frozen(Color.FromRgb(0x23, 0x23, 0x23)), Margin = new Thickness(0, 16, 0, 0), Child = eula, Visibility = create ? Visibility.Visible : Visibility.Collapsed, ToolTip = "https://aka.ms/MinecraftEULA" };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        FrameworkElement Labeled(string label, FrameworkElement control, int column)
        {
            var stack = new StackPanel { Children = { Ui.Label(label), control } };
            Grid.SetColumn(stack, column);
            return stack;
        }
        grid.Children.Add(Labeled("Version", version, 0));
        grid.Children.Add(Labeled("Port", port, 2));
        grid.Children.Add(Labeled("Max. Spieler", maxPlayers, 4));
        grid.Margin = new Thickness(0, 0, 0, 16);

        bool Validate()
        {
            var ok = name.Text.Trim().Length > 0 && version.SelectedItem is string v && !v.StartsWith("Lade")
                     && int.TryParse(port.Text, out var p) && p is >= 1024 and <= 65535
                     && int.TryParse(maxPlayers.Text, out var m) && m is >= 1 and <= 500
                     && (!create || eula.IsChecked == true);
            submit.IsEnabled = ok;
            return ok;
        }
        name.TextChanged += (_, _) => Validate();
        port.TextChanged += (_, _) => Validate();
        maxPlayers.TextChanged += (_, _) => Validate();
        version.SelectionChanged += (_, _) => Validate();
        eula.Click += (_, _) => Validate();
        submit.Click += (_, _) =>
        {
            if (!Validate())
                return;
            var chosen = (string)version.SelectedItem!;
            try
            {
                if (create)
                {
                    result = host.Create(name.Text.Trim(), software, chosen, int.Parse(port.Text), int.Parse(maxPlayers.Text),
                        (int)ram.Value * 1024, icon);
                    host.SetOpenToInternet(result!, openSwitch.IsChecked == true);
                }
                else
                {
                    host.Update(existing!, name.Text.Trim(), software, chosen, int.Parse(port.Text), int.Parse(maxPlayers.Text),
                        (int)ram.Value * 1024, icon, resetIcon);
                    host.SetOpenToInternet(existing!, openSwitch.IsChecked == true);
                    result = existing;
                }
                app.Dialogs.ClosePanel();
            }
            catch (Exception ex)
            {
                _ = app.Dialogs.ShowErrorAsync("Server konnte nicht gespeichert werden", ex);
            }
        };

        var body = new StackPanel { Margin = new Thickness(22, 16, 22, 16) };
        body.Children.Add(iconBox);
        body.Children.Add(Ui.Label("Name"));
        body.Children.Add(name);
        body.Children.Add(Ui.Label("Software"));
        body.Children.Add(new Border { Style = Ui.Resource<Style>("SegmentGroup"), Child = softwareGrid });
        body.Children.Add(softwareHint);
        body.Children.Add(grid);
        body.Children.Add(ramHead);
        body.Children.Add(ram);
        body.Children.Add(openRow);
        body.Children.Add(eulaBox);

        UIElement? left = null;
        if (!create)
        {
            var delete = DialogParts.Make("DangerTextButton", "Server löschen", () =>
            {
                app.Dialogs.ClosePanel();
                _ = DeleteAsync(app, existing!);
            });
            left = delete;
        }
        var header = DialogParts.Header(create ? "Server erstellen" : "Server bearbeiten",
            create ? "Der Server wird lokal auf diesem PC aufgesetzt. Danach kannst du ihn mit einem Klick starten."
                : "Änderungen an Software oder Version werden beim nächsten Start übernommen.",
            DialogParts.IconTile("Server"), app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var footer = DialogParts.Footer(left, DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), submit);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        _ = LoadVersionsAsync();
        Validate();
        await app.Dialogs.ShowPanelAsync(root, 520);
        return result;
    }

    public static async Task DeleteAsync(AppServices app, LocalServer server)
    {
        var text = server.Imported
            ? $"„{server.Name}“ aus der Liste entfernen? Der Ordner bleibt erhalten:\n{server.Dir}"
            : $"„{server.Name}“ löschen? Der Server-Ordner mit Welten und Einstellungen wandert in den Papierkorb.";
        if (!await app.Dialogs.ConfirmAsync("Server löschen", text, server.Imported ? "Entfernen" : "Löschen", danger: true))
            return;
        await UiRun.GuardAsync(app, "Server konnte nicht gelöscht werden", () => app.LocalServers.DeleteAsync(server, deleteFiles: true));
    }
}

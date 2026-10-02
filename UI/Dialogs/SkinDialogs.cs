using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public sealed record SkinChoice(byte[] Png, string Name, bool Slim, bool Apply);

public static class SkinDialogs
{
    private static Brush Stage()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.85),
            GradientOrigin = new Point(0.5, 0.85),
            RadiusX = 1.2,
            RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x33, 0x33, 0x33), 0),
                new GradientStop(Color.FromRgb(0x2A, 0x2A, 0x2A), 0.55),
                new GradientStop(Color.FromRgb(0x25, 0x25, 0x25), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static RadioButton Segment(string text, string group, bool on) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = on,
        Style = Ui.Resource<Style>("SegmentButton"),
        FontSize = 13,
        Padding = new Thickness(16, 0, 16, 0),
        Margin = new Thickness(0, 0, 2, 0)
    };

    public static async Task<SkinChoice?> EditAsync(AppServices app, SkinEntry? entry)
    {
        SkinChoice? result = null;
        byte[]? png = entry != null && File.Exists(entry.FilePath) ? await File.ReadAllBytesAsync(entry.FilePath) : null;
        var group = Guid.NewGuid().ToString("N");
        var classic = Segment("Classic", group, entry?.Slim != true);
        var slim = Segment("Slim", group, entry?.Slim == true);

        var viewer = new SkinViewer { Width = 200, Height = 280 };
        var noPreview = new TextBlock
        {
            Text = "Die 3D-Vorschau erscheint, sobald du eine Datei wählst",
            FontSize = 12.5,
            Foreground = Ui.Resource<Brush>("DimText"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 24, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var modelChip = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("SubtleText"), VerticalAlignment = VerticalAlignment.Center };
        var stage = new Grid { Width = 230, Height = 320 };
        stage.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Stage() });
        stage.Children.Add(viewer);
        stage.Children.Add(noPreview);
        stage.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10),
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            Background = Ui.Frozen(Color.FromArgb(0x66, 0, 0, 0)),
            Child = modelChip
        });

        var fileTitle = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), TextTrimming = TextTrimming.CharacterEllipsis };
        var fileSub = new TextBlock { FontSize = 12, Foreground = Ui.Resource<Brush>("MutedText"), Margin = new Thickness(0, 3, 0, 0) };
        var drop = new Border
        {
            Height = 92,
            CornerRadius = new CornerRadius(10),
            Background = Ui.Resource<Brush>("FieldBackground"),
            BorderBrush = Ui.Resource<Brush>("FieldBorder"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(16, 0, 16, 0),
            Cursor = Cursors.Hand,
            AllowDrop = true,
            Child = new DockPanel
            {
                Children =
                {
                    new Border
                    {
                        Width = 44, Height = 44, CornerRadius = new CornerRadius(11), Background = Ui.Resource<Brush>("AccentSoft"), Margin = new Thickness(0, 0, 14, 0),
                        Child = new Icon { Kind = "Upload", Size = 18, Foreground = Ui.Resource<Brush>("AccentText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    },
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { fileTitle, fileSub } }
                }
            }
        };
        var name = Ui.Input(entry?.Name ?? "");
        name.MaxLength = 32;
        Field.SetHint(name, "z. B. Sommer-Outfit");
        name.Margin = new Thickness(0, 0, 0, 16);
        var modelHint = new TextBlock { FontSize = 12, Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(0, 7, 0, 0) };
        var submit = DialogParts.Primary(entry == null ? "Hochladen" : "Speichern", () => { }, entry == null ? "Upload" : null);
        var fileName = entry != null ? entry.Name + ".png" : null;

        void Update()
        {
            var isSlim = slim.IsChecked == true;
            modelChip.Text = isSlim ? "Slim · 3 px Arme" : "Classic · 4 px Arme";
            modelHint.Text = isSlim ? "Schmale Arme wie bei Alex" : "Breite Arme wie bei Steve";
            Ui.Show(noPreview, png == null);
            Ui.Show(viewer, png != null);
            if (png != null)
                viewer.SetSkin(png, isSlim);
            fileTitle.Text = png == null ? "PNG auswählen oder hierher ziehen" : fileName ?? "Skin.png";
            fileSub.Text = png == null ? "64 × 64 oder 64 × 32 Pixel" : $"{png.Length / 1024.0:0.#} KB · Andere Datei wählen";
            fileSub.Foreground = png == null ? Ui.Resource<Brush>("MutedText") : Ui.Resource<Brush>("AccentText");
            submit.IsEnabled = png != null && name.Text.Trim().Length > 0;
        }

        async Task LoadAsync(string path)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (!SkinLibrary.IsValidSkin(bytes))
            {
                await app.Dialogs.ShowMessageAsync("Ungültiger Skin",
                    "Die Datei ist kein gültiger Minecraft-Skin. Erlaubt sind PNG-Bilder mit 64 × 64 (oder alt 64 × 32) Pixeln.");
                return;
            }
            png = bytes;
            fileName = Path.GetFileName(path);
            if (name.Text.Trim().Length == 0)
                name.Text = Path.GetFileNameWithoutExtension(path);
            Update();
        }

        drop.MouseLeftButtonUp += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Skin (*.png)|*.png", Title = "Skin auswählen" };
            if (dialog.ShowDialog(Application.Current.MainWindow) == true)
                await LoadAsync(dialog.FileName);
        };
        drop.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        drop.Drop += async (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                await LoadAsync(files[0]);
        };
        classic.Checked += (_, _) => Update();
        slim.Checked += (_, _) => Update();
        name.TextChanged += (_, _) => Update();
        submit.Click += (_, _) =>
        {
            if (png == null)
                return;
            result = new SkinChoice(png, name.Text.Trim(), slim.IsChecked == true, entry == null);
            app.Dialogs.ClosePanel();
        };

        var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        right.Children.Add(Ui.Label("Skin-Datei"));
        right.Children.Add(drop);
        var nameLabel = Ui.Label("Name");
        nameLabel.Margin = new Thickness(0, 16, 0, 7);
        right.Children.Add(nameLabel);
        right.Children.Add(name);
        right.Children.Add(Ui.Label("Modell"));
        right.Children.Add(new Border
        {
            Style = Ui.Resource<Style>("SegmentGroup"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { classic, slim } }
        });
        right.Children.Add(modelHint);

        var body = new DockPanel { Margin = new Thickness(22, 16, 22, 16) };
        DockPanel.SetDock(stage, Dock.Left);
        body.Children.Add(stage);
        body.Children.Add(right);

        var header = DialogParts.Header(entry == null ? "Skin hochladen" : "Skin bearbeiten",
            entry == null ? "Der Skin wird in deiner Bibliothek gespeichert und sofort angewendet." : "Name, Datei und Modell ändern.",
            DialogParts.IconTile(entry == null ? "Upload" : "Pencil"), app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var footer = DialogParts.Footer(null, DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), submit);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        Update();
        await app.Dialogs.ShowPanelAsync(root, 680);
        return result;
    }

    public static async Task<SkinChoice?> AdoptAsync(AppServices app)
    {
        SkinChoice? result = null;
        PlayerSkin? found = null;
        string? foundName = null;

        var name = Ui.Input();
        name.MaxLength = 16;
        Field.SetIcon(name, "Person");
        Field.SetHint(name, "z. B. Granulator444");
        name.Margin = new Thickness(0);
        var search = DialogParts.Make("SecondButton", "Suchen", () => { });
        search.Height = 40;
        search.Margin = new Thickness(8, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(search, Dock.Right);
        row.Children.Add(search);
        row.Children.Add(name);

        var viewer = new SkinViewer { Width = 200, Height = 240, Visibility = Visibility.Collapsed };
        var message = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12), Visibility = Visibility.Collapsed };
        var stage = new Grid { Height = 300, Margin = new Thickness(0, 16, 0, 0) };
        stage.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Stage() });
        stage.Children.Add(viewer);
        stage.Children.Add(message);
        stage.Children.Add(caption);

        var save = DialogParts.Primary("Speichern", () =>
        {
            if (found == null)
                return;
            result = new SkinChoice(found.Png, foundName!, found.Slim, false);
            app.Dialogs.ClosePanel();
        });
        save.IsEnabled = false;

        void ShowMessage(string title, string? text, bool warn = false, bool spinner = false)
        {
            message.Children.Clear();
            if (spinner)
            {
                var spin = Ui.Spinner(32);
                spin.HorizontalAlignment = HorizontalAlignment.Center;
                message.Children.Add(spin);
            }
            message.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = warn ? 13.5 : 12.5,
                FontWeight = warn ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = warn ? Ui.Frozen(Color.FromRgb(0xF0, 0xC9, 0x5A)) : Ui.Resource<Brush>(spinner ? "MutedText" : "DimText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, spinner ? 10 : 0, 0, 0)
            });
            if (text != null)
                message.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Ui.Resource<Brush>("MutedText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            Ui.Show(message, true);
            Ui.Show(viewer, false);
            Ui.Show(caption, false);
        }

        async Task SearchAsync()
        {
            var query = name.Text.Trim();
            if (query.Length == 0)
                return;
            found = null;
            save.IsEnabled = false;
            ShowMessage("Lade Skin …", null, spinner: true);
            try
            {
                var player = await PlayerSkins.LookUpAsync(app.Http, query);
                var skin = player is { } p ? await PlayerSkins.LoadAsync(app.Http, p.Uuid) : null;
                if (player == null || skin == null)
                {
                    ShowMessage("Spieler nicht gefunden", "Prüfe die Schreibweise des Namens.", warn: true);
                    return;
                }
                found = skin;
                foundName = player.Value.Name;
                viewer.SetSkin(skin.Png, skin.Slim);
                caption.Children.Clear();
                caption.Children.Add(new TextBlock { Text = foundName, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), VerticalAlignment = VerticalAlignment.Center });
                caption.Children.Add(new Border
                {
                    Height = 20,
                    Padding = new Thickness(7, 0, 7, 0),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(8, 0, 0, 0),
                    Background = Ui.Frozen(Color.FromArgb(0x66, 0, 0, 0)),
                    Child = new TextBlock { Text = skin.Slim ? "Slim" : "Classic", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("SubtleText"), VerticalAlignment = VerticalAlignment.Center }
                });
                Ui.Show(message, false);
                Ui.Show(viewer, true);
                Ui.Show(caption, true);
                save.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ShowMessage("Suche fehlgeschlagen", ErrorReport.Short(ex), warn: true);
            }
        }

        search.Click += async (_, _) => await SearchAsync();
        name.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            await SearchAsync();
        };
        ShowMessage("Hier erscheint die 3D-Vorschau des Skins", null);

        var body = new StackPanel { Margin = new Thickness(22, 16, 22, 16), Children = { row, stage } };
        var header = DialogParts.Header("Skin von Spieler übernehmen",
            "Gib den Minecraft-Namen ein. Der Skin wird in deine Bibliothek kopiert.", DialogParts.IconTile("Person"),
            app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var footer = DialogParts.Footer(null, DialogParts.Secondary("Abbrechen", app.Dialogs.ClosePanel), save);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        await app.Dialogs.ShowPanelAsync(root, 460);
        return result;
    }
}

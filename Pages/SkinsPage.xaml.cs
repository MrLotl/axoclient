using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace McLauncher.Pages;

/// <summary>Skin ändern (Datei oder Bibliothek) und Umhang wählen.</summary>
public partial class SkinsPage : UserControl, INotifyPropertyChanged
{
    private AppState _app = null!;
    private SkinLibrary _library = null!;
    private byte[]? _newSkin;
    private bool _busy;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Skins können nur angemeldet und nicht während einer laufenden Änderung angewendet werden.</summary>
    public bool CanApply => _app?.Session != null && !_busy;

    public SkinsPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void Initialize(AppState app)
    {
        _app = app;
        _library = new SkinLibrary(AppState.LauncherDir);
        _app.AccountChanged += UpdateAccount;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _app.Session != null)
                _ = _app.RefreshClientCapesAsync();
        };
        UpdateAccount();
        RefreshLibrary();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApply)));
        ApplyNewButton.IsEnabled = CanApply;
        SaveCurrentButton.IsEnabled = _app.Profile?.SkinPng != null;
        foreach (var child in CapePanel.Children.OfType<RadioButton>())
            child.IsEnabled = CanApply;
        foreach (var child in ClientCapePanel.Children.OfType<RadioButton>())
            child.IsEnabled = CanApply && _app.Friends.Available;
    }

    private void UpdateAccount()
    {
        var profile = _app.Profile;
        LoginHint.Visibility = _app.Session == null ? Visibility.Visible : Visibility.Collapsed;
        CurrentSkin.SetSkin(profile?.SkinPng, profile?.SkinSlim ?? false, _app.DisplayCapePng);
        CurrentVariant.Text = profile?.SkinPng == null ? "" : profile.SkinSlim ? "Schmale Arme (Alex)" : "Breite Arme (Steve)";
        RefreshCapes();
        UpdateButtons();
    }

    // ---------- Umhang ----------

    private void RefreshCapes()
    {
        // Minecraft-Umhänge des Kontos
        CapePanel.Children.Clear();
        var capes = _app.Profile?.Capes ?? [];
        CapesEmpty.Visibility = _app.Session != null && capes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (capes.Count > 0)
        {
            AddCapeOption(CapePanel, "Kein Umhang", null, null, capes.All(c => !c.Active), () => _app.SetCapeAsync(null));
            foreach (var cape in capes)
                AddCapeOption(CapePanel, cape.Alias, cape.Id, cape.Png, cape.Active, () => _app.SetCapeAsync(cape.Id));
        }

        // AxoClient-Umhänge (für alle frei wählbar, gespeichert beim AxoClient-Dienst)
        ClientCapePanel.Children.Clear();
        if (_app.ClientCapes.Count > 0)
        {
            AddCapeOption(ClientCapePanel, "Keiner", null, null, _app.ClientCapeId == null,
                () => _app.SetClientCapeAsync(null));
            foreach (var cape in _app.ClientCapes)
                AddCapeOption(ClientCapePanel, cape.Name, cape.Id, cape.Png, cape.Id == _app.ClientCapeId,
                    () => _app.SetClientCapeAsync(cape.Id));
        }
    }

    /// <summary>Kachel mit Vorschaubild (Außenseite des Umhangs) und Namen; <paramref name="apply"/> legt ihn an.</summary>
    private void AddCapeOption(Panel panel, string label, string? capeId, byte[]? png, bool active, Func<Task> apply)
    {
        var preview = new Grid { Width = 50, Height = 80, Margin = new Thickness(0, 4, 0, 8) };
        if (png != null && SkinRenderer.RenderCape(png) is { } image)
        {
            var img = new Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
            preview.Children.Add(img);
        }
        else
        {
            // Kein Umhang bzw. Bild nicht geladen: Symbol statt Vorschau
            preview.Children.Add(new TextBlock
            {
                Text = ((char)(capeId == null ? 0xE711 : 0xE7B8)).ToString(), // Symbole: "Kein" bzw. Umhang
                FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
                FontSize = 24,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var content = new StackPanel { Width = 90 };
        content.Children.Add(preview);
        content.Children.Add(new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            FontSize = 12
        });

        var radio = new RadioButton
        {
            Content = content,
            GroupName = panel.Name, // eigene Auswahl je Bereich
            IsChecked = active,
            ToolTip = label,
            Style = (Style)FindResource("SegmentButton"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 8)
        };
        radio.Checked += async (_, _) =>
        {
            if (_busy)
                return;
            SetBusy(true);
            StatusText.Text = "Ändere Umhang...";
            try
            {
                await apply();
                StatusText.Text = panel == ClientCapePanel
                    ? "AxoClient-Umhang geändert. Andere Spieler sehen ihn spätestens nach 10 Minuten."
                    : "Umhang geändert.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                await _app.Dialogs.ShowMessageAsync("Umhang konnte nicht geändert werden", ex.Message);
                RefreshCapes();
            }
            finally
            {
                SetBusy(false);
            }
        };
        panel.Children.Add(radio);
    }

    // ---------- Bibliothek ----------

    private void RefreshLibrary()
    {
        var entries = _library.Load();
        LibraryList.ItemsSource = entries;
        LibraryEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Profile?.SkinPng is not { } png)
            return;
        _library.Add(png, _app.Session?.Username ?? "Mein Skin", _app.Profile.SkinSlim);
        RefreshLibrary();
        StatusText.Text = "Aktueller Skin wurde gespeichert.";
    }

    private async void ApplySkin_Click(object sender, RoutedEventArgs e)
    {
        var entry = (SkinEntry)((FrameworkElement)sender).DataContext;
        await UploadAsync(File.ReadAllBytes(entry.FilePath), entry.Slim, entry.Name);
    }

    private async void RemoveSkin_Click(object sender, RoutedEventArgs e)
    {
        var entry = (SkinEntry)((FrameworkElement)sender).DataContext;
        if (!await _app.Dialogs.ConfirmAsync("Skin entfernen",
                $"\"{entry.Name}\" aus der Bibliothek entfernen?", "Entfernen", danger: true))
            return;
        _library.Remove(entry);
        RefreshLibrary();
    }

    // ---------- Neuer Skin ----------

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await LoadNewSkinAsync(files[0]);
    }

    private async void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Skin (*.png)|*.png", Title = "Skin auswählen" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await LoadNewSkinAsync(dialog.FileName);
    }

    private async Task LoadNewSkinAsync(string path)
    {
        var png = await File.ReadAllBytesAsync(path);
        if (!SkinLibrary.IsValidSkin(png))
        {
            await _app.Dialogs.ShowMessageAsync("Ungültiger Skin",
                "Die Datei ist kein gültiger Minecraft-Skin. Erlaubt sind PNG-Bilder mit 64×64 (oder alt 64×32) Pixeln.");
            return;
        }

        _newSkin = png;
        NewSkinName.Text = Path.GetFileNameWithoutExtension(path);
        ClassicRadio.IsChecked = true;
        UpdateNewPreview();
        DropText.Text = Path.GetFileName(path);
        NewSkinPreview.Visibility = Visibility.Visible;
        NewSkinOptions.Visibility = Visibility.Visible;
    }

    private void Variant_Checked(object sender, RoutedEventArgs e) => UpdateNewPreview();

    private void UpdateNewPreview()
    {
        if (_newSkin != null)
            NewSkinPreview.Source = SkinRenderer.RenderFront(_newSkin, SlimRadio.IsChecked == true);
    }

    private string NewName => string.IsNullOrWhiteSpace(NewSkinName.Text) ? "Skin" : NewSkinName.Text.Trim();

    private async void ApplyNew_Click(object sender, RoutedEventArgs e)
    {
        if (_newSkin == null)
            return;
        _library.Add(_newSkin, NewName, SlimRadio.IsChecked == true);
        RefreshLibrary();
        if (await UploadAsync(_newSkin, SlimRadio.IsChecked == true, NewName))
            ResetNewSkin();
    }

    private void SaveNew_Click(object sender, RoutedEventArgs e)
    {
        if (_newSkin == null)
            return;
        _library.Add(_newSkin, NewName, SlimRadio.IsChecked == true);
        RefreshLibrary();
        StatusText.Text = $"\"{NewName}\" wurde in der Bibliothek gespeichert.";
        ResetNewSkin();
    }

    private void DiscardNew_Click(object sender, RoutedEventArgs e) => ResetNewSkin();

    private void ResetNewSkin()
    {
        _newSkin = null;
        NewSkinPreview.Visibility = Visibility.Collapsed;
        NewSkinOptions.Visibility = Visibility.Collapsed;
        DropText.Text = "Skin-Datei (PNG) hierher ziehen oder klicken";
    }

    private async Task<bool> UploadAsync(byte[] png, bool slim, string name)
    {
        if (!CanApply)
            return false;
        SetBusy(true);
        StatusText.Text = $"Lade \"{name}\" hoch...";
        try
        {
            await _app.UploadSkinAsync(png, slim);
            StatusText.Text = $"\"{name}\" ist jetzt dein Skin. Im Spiel wird er ab dem nächsten Start angezeigt.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowMessageAsync("Skin konnte nicht geändert werden", ex.Message);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace AxoClient.UI.Pages;

public partial class SkinsPage : UserControl, INotifyPropertyChanged
{
    private AppServices _app = null!;
    private SkinLibrary _library = null!;
    private byte[]? _newSkin;
    private bool _busy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanApply => _app?.Accounts.Session != null && !_busy;

    public SkinsPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _library = new SkinLibrary();
        _app.Accounts.Changed += UpdateAccount;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _app.Accounts.Session != null)
                _ = _app.Capes.RefreshAsync();
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
        SaveCurrentButton.IsEnabled = _app.Accounts.Profile?.SkinPng != null;
        foreach (var child in CapePanel.Children.OfType<RadioButton>())
            child.IsEnabled = CanApply;
        foreach (var child in ClientCapePanel.Children.OfType<ButtonBase>())
            child.IsEnabled = CanApply && _app.Axo.Available;
    }

    private void UpdateAccount()
    {
        LoginHint.Visibility = _app.Accounts.Session == null ? Visibility.Visible : Visibility.Collapsed;
        RefreshCapes();
        UpdatePreview(force: true);
        UpdateButtons();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SkinsView == null)
            return;
        var skins = SkinsTab.IsChecked == true;
        SkinsView.Visibility = skins ? Visibility.Visible : Visibility.Collapsed;
        McCapesView.Visibility = McCapesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AxoCapesView.Visibility = AxoCapesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveCurrentButton.Visibility = skins ? Visibility.Visible : Visibility.Collapsed;
        CapeModePanel.Visibility = skins ? Visibility.Collapsed : Visibility.Visible;
        PreviewTitle.Text = skins ? "Aktueller Skin" : "Vorschau";
        Scroller.ScrollToTop();

        CurrentSkin.TurnTo(skins ? SkinViewer.FrontYaw : SkinViewer.BackYaw);
        _hoverCape = null;
        UpdatePreview();
    }

    private (byte[]? Png, string Name)? _hoverCape;

    private byte[]? _shownSkin, _shownCape;
    private bool _shownElytra;

    private void UpdatePreview(bool force = false)
    {
        var profile = _app.Accounts.Profile;
        var mc = McCapesTab.IsChecked == true;
        var cape = _hoverCape is { } hover ? hover.Png : mc ? profile?.ActiveCapePng : _app.Capes.DisplayPng;
        var elytra = CapeModeElytra.IsChecked == true && SkinsTab.IsChecked != true;
        if (force || !ReferenceEquals(_shownSkin, profile?.SkinPng) || !ReferenceEquals(_shownCape, cape) ||
            elytra != _shownElytra)
        {
            CurrentSkin.SetSkin(profile?.SkinPng, profile?.SkinSlim ?? false, cape, elytra);
            _shownSkin = profile?.SkinPng;
            _shownCape = cape;
            _shownElytra = elytra;
        }

        if (profile?.SkinPng == null)
            CurrentVariant.Text = "";
        else if (SkinsTab.IsChecked == true)
            CurrentVariant.Text = profile.SkinSlim ? "Schmale Arme (Alex)" : "Breite Arme (Steve)";
        else if (_hoverCape is { } tried)
            CurrentVariant.Text = "Anprobe: " + tried.Name;
        else
        {
            var name = mc
                ? profile.ActiveCapeName
                : _app.Capes.Selected?.Name
                  ?? profile.ActiveCapeName;
            CurrentVariant.Text = name == null ? "Kein Umhang" : (elytra ? "Elytra: " : "Umhang: ") + name;
        }
    }

    private void CapeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_app == null)
            return;
        UpdatePreview();
        CurrentSkin.TurnTo(SkinViewer.BackYaw);
    }

    private void RefreshCapes()
    {
        _hoverCape = null;

        CapePanel.Children.Clear();
        var capes = _app.Accounts.Profile?.Capes ?? [];
        CapesEmpty.Visibility = _app.Accounts.Session != null && capes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (capes.Count > 0)
        {
            AddCapeOption(CapePanel, "Kein Umhang", null, null, capes.All(c => !c.Active), () => _app.Accounts.SetCapeAsync(null));
            foreach (var cape in capes)
                AddCapeOption(CapePanel, cape.Alias, cape.Id, cape.Png, cape.Active, () => _app.Accounts.SetCapeAsync(cape.Id));
        }

        ClientCapePanel.Children.Clear();
        ClientCapesEmpty.Visibility = _app.Capes.All.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_app.Capes.All.Count > 0)
        {
            AddCapeOption(ClientCapePanel, "Keiner", null, null, _app.Capes.SelectedId == null,
                () => _app.Capes.SelectAsync(null));
            foreach (var cape in _app.Capes.All)
                AddCapeOption(ClientCapePanel, cape.Name, cape.Id, cape.Png, cape.Id == _app.Capes.SelectedId,
                    () => _app.Capes.SelectAsync(cape.Id),
                    _app.Capes.Status.IsAdmin ? () => DeleteClientCapeAsync(cape) : null);
        }
        if (_app.Capes.Status.IsAdmin)
            AddUploadCard();
        Ui.Show(AdminsButton, _app.Capes.Status.IsOwner);
    }

    private void AddCapeOption(Panel panel, string label, string? capeId, byte[]? png, bool active, Func<Task> apply,
        Func<Task>? delete = null)
    {
        var preview = new Grid { Width = 50, Height = 80, Margin = new Thickness(0, 4, 0, 8) };
        if (png != null && SkinRenderer.RenderCape(png) is { } image)
        {
            var img = new Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, image.PixelWidth > 20
                ? System.Windows.Media.BitmapScalingMode.HighQuality
                : System.Windows.Media.BitmapScalingMode.NearestNeighbor);
            preview.Children.Add(img);
        }
        else
        {
            preview.Children.Add(new TextBlock
            {
                Text = ((char)(capeId == null ? 0xE711 : 0xE7B8)).ToString(),
                FontFamily = Ui.Resource<System.Windows.Media.FontFamily>("IconFont"),
                FontSize = 24,
                Foreground = Ui.Resource<System.Windows.Media.Brush>("MutedText"),
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

        var card = new Grid();
        card.Children.Add(content);
        if (delete != null)
        {
            var remove = new Button
            {
                Style = Ui.Resource<Style>("IconButton"),
                Content = "",
                Width = 24,
                Height = 24,
                FontSize = 11,
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Umhang für alle löschen"
            };
            remove.Click += async (_, e) =>
            {
                e.Handled = true;
                await delete();
            };
            card.Children.Add(remove);
        }

        var radio = new RadioButton
        {
            Content = card,
            GroupName = panel.Name,
            IsChecked = active,
            ToolTip = label,
            Style = Ui.Resource<Style>("SegmentButton"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 8)
        };
        radio.MouseEnter += (_, _) =>
        {
            _hoverCape = (png, label);
            UpdatePreview();
        };
        radio.MouseLeave += (_, _) =>
        {
            _hoverCape = null;
            UpdatePreview();
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
                await _app.Dialogs.ShowErrorAsync("Umhang konnte nicht geändert werden", ex);
                RefreshCapes();
            }
            finally
            {
                SetBusy(false);
            }
        };
        panel.Children.Add(radio);
    }

    private void AddUploadCard()
    {
        var content = new StackPanel { Width = 90 };
        content.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 40,
            FontWeight = FontWeights.Light,
            Height = 80,
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(0, 12, 0, 0),
            TextAlignment = TextAlignment.Center,
            Foreground = Ui.Resource<System.Windows.Media.Brush>("MutedText")
        });
        content.Children.Add(new TextBlock { Text = "Hochladen", TextAlignment = TextAlignment.Center, FontSize = 12 });

        var button = new Button
        {
            Content = content,
            Style = Ui.Resource<Style>("LauncherButton"),
            Background = Ui.Frozen(System.Windows.Media.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            Foreground = Ui.Resource<System.Windows.Media.Brush>("SubtleText"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 12, 8),
            ToolTip = "Neuen AxoClient-Umhang hochladen",
            IsEnabled = CanApply && _app.Axo.Available
        };
        button.Click += async (_, _) => await UploadClientCapeAsync();
        ClientCapePanel.Children.Add(button);
    }

    private async Task UploadClientCapeAsync()
    {
        if (_busy || await CapeUploadDialog.AskAsync(_app) is not { } cape)
            return;
        SetBusy(true);
        StatusText.Text = $"Lade \"{cape.Name}\" hoch...";
        try
        {
            await _app.Capes.UploadAsync(cape.Name, cape.Png);
            StatusText.Text = $"\"{cape.Name}\" ist hochgeladen. Alle AxoClient-Nutzer können ihn jetzt auswählen.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowErrorAsync("Umhang konnte nicht hochgeladen werden", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteClientCapeAsync(ClientCape cape)
    {
        if (_busy || !await _app.Dialogs.ConfirmAsync("Umhang löschen",
                $"\"{cape.Name}\" für alle löschen? Wer ihn gerade trägt, hat danach keinen AxoClient-Umhang mehr.",
                "Löschen", danger: true))
            return;
        SetBusy(true);
        StatusText.Text = $"Lösche \"{cape.Name}\"...";
        try
        {
            await _app.Capes.DeleteAsync(cape);
            StatusText.Text = $"\"{cape.Name}\" wurde gelöscht.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowErrorAsync("Umhang konnte nicht gelöscht werden", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Admins_Click(object sender, RoutedEventArgs e) => await AdminsDialog.ShowAsync(_app);

    private void RefreshLibrary()
    {
        var entries = _library.Load();
        LibraryList.ItemsSource = entries;
        LibraryEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Accounts.Profile?.SkinPng is not { } png)
            return;
        _library.Add(png, _app.Accounts.Session?.Username ?? "Mein Skin", _app.Accounts.Profile.SkinSlim);
        RefreshLibrary();
        StatusText.Text = "Aktueller Skin wurde gespeichert.";
    }

    private async void ApplySkin_Click(object sender, RoutedEventArgs e)
    {
        var entry = Ui.DataOf<SkinEntry>(sender);
        await UploadAsync(File.ReadAllBytes(entry.FilePath), entry.Slim, entry.Name);
    }

    private async void RemoveSkin_Click(object sender, RoutedEventArgs e)
    {
        var entry = Ui.DataOf<SkinEntry>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Skin entfernen",
                $"\"{entry.Name}\" aus der Bibliothek entfernen?", "Entfernen", danger: true))
            return;
        _library.Remove(entry);
        RefreshLibrary();
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
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
        NewSkinOptions.Visibility = Visibility.Visible;
    }

    private void Variant_Checked(object sender, RoutedEventArgs e) => UpdateNewPreview();

    private void UpdateNewPreview()
    {
        if (_newSkin != null)
            NewSkinPreview.SetSkin(_newSkin, SlimRadio.IsChecked == true);
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
        NewSkinPreview.SetSkin(null, false);
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
            await _app.Accounts.UploadSkinAsync(png, slim);
            StatusText.Text = $"\"{name}\" ist jetzt dein Skin. Im Spiel wird er ab dem nächsten Start angezeigt.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowErrorAsync("Skin konnte nicht geändert werden", ex);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }
}

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
    }

    private void UpdateAccount()
    {
        var profile = _app.Profile;
        LoginHint.Visibility = _app.Session == null ? Visibility.Visible : Visibility.Collapsed;
        CurrentSkin.Source = profile?.SkinFront;
        CurrentVariant.Text = profile?.SkinPng == null ? "" : profile.SkinSlim ? "Schmale Arme (Alex)" : "Breite Arme (Steve)";
        RefreshCapes();
        UpdateButtons();
    }

    // ---------- Umhang ----------

    private void RefreshCapes()
    {
        CapePanel.Children.Clear();
        var capes = _app.Profile?.Capes ?? [];
        CapesEmpty.Visibility = _app.Session != null && capes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (capes.Count == 0)
            return;

        AddCapeOption("Kein Umhang", null, capes.All(c => !c.Active));
        foreach (var cape in capes)
            AddCapeOption(cape.Alias, cape.Id, cape.Active);
    }

    private void AddCapeOption(string label, string? capeId, bool active)
    {
        var radio = new RadioButton
        {
            Content = label,
            GroupName = "Cape",
            IsChecked = active,
            Style = (Style)FindResource("SegmentButton"),
            Margin = new Thickness(0, 0, 6, 6)
        };
        radio.Checked += async (_, _) =>
        {
            if (_busy)
                return;
            SetBusy(true);
            StatusText.Text = "Ändere Umhang...";
            try
            {
                await _app.SetCapeAsync(capeId);
                StatusText.Text = "Umhang geändert.";
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
        CapePanel.Children.Add(radio);
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

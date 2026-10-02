using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Pages;

public sealed class SkinItem(SkinEntry entry, bool active)
{
    public SkinEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Source => Entry.Source;
    public BitmapSource? Preview => Entry.Preview;
    public bool Active { get; } = active;
    public string ApplyTip => Active ? "Das ist dein aktueller Skin" : $"„{Entry.Name}“ als Skin verwenden";
}

public sealed class CapeItem : ISpecialItem
{
    public required string Kind { get; init; }
    public string? Id { get; init; }
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";
    public byte[]? Png { get; init; }
    public BitmapSource? Image { get; init; }
    public bool Active { get; init; }
    public bool CanDelete { get; init; }
    public string? LinkLabel { get; init; }
    public ClientCape? Cape { get; init; }

    public bool HasImage => Image != null;
    public bool IsNone => Image == null && Kind != "upload";
    public bool HasLink => LinkLabel != null;
    public bool IsSpecial => Kind == "upload";
    public string Tip => Active ? $"{Name} ist aktiv" : $"{Name} auswählen";
}

public partial class SkinsPage : UserControl
{
    private AppServices _app = null!;
    private SkinLibrary _library = null!;
    private bool _busy;
    private CapeItem? _hover;
    private byte[]? _shownSkin, _shownCape;
    private bool _shownElytra;

    public SkinsPage()
    {
        InitializeComponent();
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _library = new SkinLibrary();
        _app.Accounts.Changed += () => Dispatcher.InvokeAsync(UpdateAccount);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _app.Accounts.Session != null)
                _ = _app.Capes.RefreshAsync();
        };
        UpdateAccount();
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        Ui.Show(StatusText, text.Length > 0);
    }

    private void UpdateAccount()
    {
        Ui.Show(LoginHint, _app.Accounts.Session == null);
        if (_app.Accounts.Profile?.SkinPng is { } current && _app.Accounts.Session is { } session
            && _library.Load().All(e => e.Id != SkinLibrary.IdOf(current)))
            _library.Add(current, session.Username, _app.Accounts.Profile.SkinSlim);
        RefreshLibrary();
        RefreshCapes();
        UpdatePreview(force: true);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SkinsView == null)
            return;
        var skins = SkinsTab.IsChecked == true;
        Ui.Show(SkinsView, skins);
        Ui.Show(CapesView, !skins);
        Ui.Show(SkinActions, skins);
        Ui.Show(CapeActions, !skins);
        Ui.Show(WearToggle, !skins);
        PreviewTitle.Text = skins ? "AKTUELLER SKIN" : "VORSCHAU";
        UpdateCount();
        CurrentSkin.TurnTo(skins ? SkinViewer.FrontYaw : SkinViewer.BackYaw);
        _hover = null;
        UpdatePreview();
    }

    private void Wear_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        CurrentSkin.TurnTo(SkinViewer.BackYaw);
    }

    private void UpdateCount()
    {
        if (SkinsTab.IsChecked == true)
            CountText.Text = Formats.Count(LibraryList.Items.Count, "Skin", "Skins");
        else
            CountText.Text = Formats.Count((_app.Accounts.Profile?.Capes.Count ?? 0) + _app.Capes.Global.Count() + _app.Capes.Mine.Count(),
                "Cape", "Capes");
    }

    private void UpdatePreview(bool force = false)
    {
        var profile = _app.Accounts.Profile;
        var cape = _hover is { } hover ? hover.Png : _app.Capes.DisplayPng;
        var elytra = CapesTab.IsChecked == true && WearElytra.IsChecked == true;
        if (force || !ReferenceEquals(_shownSkin, profile?.SkinPng) || !ReferenceEquals(_shownCape, cape) || _shownElytra != elytra)
        {
            CurrentSkin.SetSkin(profile?.SkinPng, profile?.SkinSlim ?? false, cape, elytra);
            _shownSkin = profile?.SkinPng;
            _shownCape = cape;
            _shownElytra = elytra;
        }
        var activeSkin = profile?.SkinPng is { } png
            ? _library.Load().FirstOrDefault(e => e.Id == SkinLibrary.IdOf(png))?.Name ?? _app.Accounts.Session?.Username
            : null;
        ActiveSkinName.Text = activeSkin ?? "–";
        ActiveCapeName.Text = _hover?.Name ?? _app.Capes.Selected?.Name ?? profile?.ActiveCapeName ?? "Kein Cape";
    }

    private void RefreshLibrary()
    {
        var activeId = _app.Accounts.Profile?.SkinPng is { } png ? SkinLibrary.IdOf(png) : null;
        var items = _library.Load().Select(e => new SkinItem(e, e.Id == activeId)).ToList();
        LibraryList.ItemsSource = items;
        Ui.Show(LibraryEmpty, items.Count == 0);
        UpdateCount();
    }

    private void RefreshCapes()
    {
        var profile = _app.Accounts.Profile;
        var mojang = profile?.Capes ?? [];
        var mc = new List<CapeItem>
        {
            new() { Kind = "mc-none", Name = "Kein Cape", Source = "Für alle ausblenden", Active = mojang.All(c => !c.Active) }
        };
        mc.AddRange(mojang.Select(c => new CapeItem
        {
            Kind = "mc",
            Id = c.Id,
            Name = c.Alias,
            Source = "Von deinem Microsoft-Konto",
            Png = c.Png,
            Image = c.Png != null ? SkinRenderer.RenderCape(c.Png) : null,
            Active = c.Active
        }));
        McCapes.ItemsSource = mc;

        var activeMojang = mojang.FirstOrDefault(c => c.Active);
        var axo = new List<CapeItem>
        {
            new()
            {
                Kind = "axo-mojang",
                Name = "Mojang-Cape nutzen",
                Source = activeMojang != null ? "Wie in Vanilla" : "Gerade kein Mojang-Cape gewählt",
                Png = activeMojang?.Png,
                Image = activeMojang?.Png != null ? SkinRenderer.RenderCape(activeMojang.Png) : null,
                LinkLabel = "Mojang",
                Active = _app.Capes.SelectedId == null
            }
        };
        foreach (var cape in _app.Capes.Global.Concat(_app.Capes.Mine))
            axo.Add(new CapeItem
            {
                Kind = "axo",
                Id = cape.Id,
                Name = cape.Name,
                Source = cape.IsPersonal ? "Eigenes Cape" : "AxoClient",
                Png = cape.Png,
                Image = cape.Png != null ? SkinRenderer.RenderCape(cape.Png) : null,
                Active = cape.Id == _app.Capes.SelectedId,
                CanDelete = _app.Capes.CanDelete(cape),
                Cape = cape
            });
        axo.Add(new CapeItem { Kind = "upload" });
        AxoCapes.ItemsSource = axo;
        AxoHint.Text = _app.Axo.Available ? "" : "Schalte unter Einstellungen „AxoClient-Symbol in der Tabliste“ ein, um AxoClient-Capes zu nutzen.";
        Ui.Show(AxoHint, !_app.Axo.Available);
        Ui.Show(AdminsButton, _app.Capes.Status.IsOwner);
        UpdateCount();
    }

    private async void ApplySkin_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<SkinItem>(sender);
        if (item.Active)
            return;
        await UploadAsync(await File.ReadAllBytesAsync(item.Entry.FilePath), item.Entry.Slim, item.Name);
    }

    private async void EditSkin_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<SkinItem>(sender);
        if (await SkinDialogs.EditAsync(_app, item.Entry) is not { } choice)
            return;
        var pngChanged = SkinLibrary.IdOf(choice.Png) != item.Entry.Id;
        _library.Update(item.Entry, choice.Name, choice.Slim, pngChanged ? choice.Png : null);
        RefreshLibrary();
        if (item.Active && (pngChanged || choice.Slim != item.Entry.Slim))
            await UploadAsync(choice.Png, choice.Slim, choice.Name);
        UpdatePreview();
    }

    private async void RemoveSkin_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<SkinItem>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Skin entfernen", $"„{item.Name}“ aus der Bibliothek entfernen?", "Entfernen", danger: true))
            return;
        _library.Remove(item.Entry);
        RefreshLibrary();
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (await SkinDialogs.EditAsync(_app, null) is not { } choice)
            return;
        _library.Add(choice.Png, choice.Name, choice.Slim);
        RefreshLibrary();
        await UploadAsync(choice.Png, choice.Slim, choice.Name);
    }

    private async void Adopt_Click(object sender, RoutedEventArgs e)
    {
        if (await SkinDialogs.AdoptAsync(_app) is not { } choice)
            return;
        _library.Add(choice.Png, choice.Name, choice.Slim, choice.Name);
        RefreshLibrary();
        ShowStatus($"Der Skin von {choice.Name} liegt jetzt in deiner Bibliothek. Klicke ihn an, um ihn zu tragen.");
    }

    private async Task<bool> UploadAsync(byte[] png, bool slim, string name)
    {
        if (_busy || _app.Accounts.Session == null)
            return false;
        _busy = true;
        ShowStatus($"Lade „{name}“ hoch …");
        try
        {
            await _app.Accounts.UploadSkinAsync(png, slim);
            ShowStatus($"„{name}“ ist jetzt dein Skin. Im Spiel wird er ab dem nächsten Start angezeigt.");
            RefreshLibrary();
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Skin konnte nicht geändert werden", ex);
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    private void Cape_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hover = Ui.DataOf<CapeItem>(sender);
        UpdatePreview();
    }

    private void Cape_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hover = null;
        UpdatePreview();
    }

    private async void Cape_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<CapeItem>(sender);
        if (item.Active || _busy || _app.Accounts.Session == null)
            return;
        _busy = true;
        ShowStatus("Ändere Cape …");
        try
        {
            switch (item.Kind)
            {
                case "mc-none":
                    await _app.Accounts.SetCapeAsync(null);
                    break;
                case "mc":
                    await _app.Accounts.SetCapeAsync(item.Id);
                    break;
                case "axo-mojang":
                    await _app.Capes.SelectAsync(null);
                    break;
                default:
                    await _app.Capes.SelectAsync(item.Id);
                    break;
            }
            ShowStatus(item.Kind.StartsWith("axo") ? "AxoClient-Cape geändert. Andere Spieler sehen es spätestens nach 10 Minuten." : "Cape geändert.");
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Cape konnte nicht geändert werden", ex);
        }
        finally
        {
            _busy = false;
            RefreshCapes();
            UpdatePreview();
        }
    }

    private async void DeleteCape_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<CapeItem>(sender);
        if (item.Cape is not { } cape || _busy)
            return;
        var who = cape.IsPersonal ? "" : " für alle";
        if (!await _app.Dialogs.ConfirmAsync("Cape löschen",
                $"„{cape.Name}“{who} löschen? Wer es gerade trägt, hat danach kein AxoClient-Cape mehr.", "Löschen", danger: true))
            return;
        _busy = true;
        try
        {
            await _app.Capes.DeleteAsync(cape);
            ShowStatus($"„{cape.Name}“ wurde gelöscht.");
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Cape konnte nicht gelöscht werden", ex);
        }
        finally
        {
            _busy = false;
            RefreshCapes();
        }
    }

    private async void NewCape_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!_app.Axo.Available)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht möglich",
                "Eigene Capes brauchen den AxoClient-Dienst. Melde dich an und schalte unter Einstellungen „AxoClient-Symbol in der Tabliste“ ein.");
            return;
        }
        if (await CapeEditorDialog.ShowAsync(_app) is not { } cape)
            return;
        _busy = true;
        ShowStatus($"Lade „{cape.Name}“ hoch …");
        try
        {
            var id = await _app.Capes.UploadAsync(cape.Name, cape.Png, cape.Global);
            await _app.Capes.SelectAsync(id);
            ShowStatus($"„{cape.Name}“ ist hochgeladen und aktiv.");
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Cape konnte nicht hochgeladen werden", ex);
        }
        finally
        {
            _busy = false;
            RefreshCapes();
            UpdatePreview();
        }
    }

    private async void Admins_Click(object sender, RoutedEventArgs e) => await AdminsDialog.ShowAsync(_app);
}

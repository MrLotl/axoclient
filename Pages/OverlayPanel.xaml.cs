using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>
/// Overlay-Profile einer Instanz verwalten: aktivieren, Server zuordnen, kopieren, teilen, löschen. Eingestellt wird
/// das Overlay selbst im Spiel; "Standard" gibt es immer.
/// </summary>
public partial class OverlayPanel : UserControl
{
    /// <summary>Eine Zeile in der Profilliste.</summary>
    public sealed record ProfileRow(string Name, bool IsActive, bool IsDefault, string Summary)
    {
        public bool CanActivate => !IsActive;
        public bool CanDelete => !IsDefault;
    }

    private AppState _app = null!;
    private Installation? _inst;

    public OverlayPanel()
    {
        InitializeComponent();
    }

    public void Show(AppState app, Installation inst)
    {
        _app = app;
        _inst = inst;
        OverlayConfigFile.EnsureDefaultProfile(inst);
        Refresh();
    }

    private bool Running => _inst != null && _app.IsRunning(_inst);

    private void Refresh()
    {
        if (_inst == null)
            return;
        var active = OverlayConfigFile.ActiveProfile(_inst);
        var rows = new List<ProfileRow>();
        foreach (var name in OverlayConfigFile.ProfileNames(_inst))
        {
            var servers = OverlayConfigFile.ReadProfile(_inst, name) is { } json
                ? OverlayProfile.Parse(json.GetRawText()).Servers
                : [];
            var summary = name == OverlayConfigFile.DefaultProfile
                ? "Gilt überall, wo kein anderes Profil passt"
                : servers.Count > 0 ? "Automatisch auf: " + string.Join(", ", servers) : "Wechselt nur von Hand";
            rows.Add(new ProfileRow(name, name.Equals(active, StringComparison.OrdinalIgnoreCase),
                name == OverlayConfigFile.DefaultProfile, summary));
        }
        ProfileItems.ItemsSource = rows;
        RunningNote.Visibility = Running ? Visibility.Visible : Visibility.Collapsed;
        ProfileItems.IsEnabled = !Running;
    }

    private static string NameOf(object sender) => ((ProfileRow)((FrameworkElement)sender).DataContext).Name;

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        OverlayConfigFile.Activate(_inst, NameOf(sender));
        Refresh();
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (_inst != null)
            await ShareUi.ShareInstanceAsync(_app, _inst, overlayProfile: NameOf(sender));
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        var source = NameOf(sender);
        var name = await AskNameAsync("Profil kopieren", OverlayConfigFile.CleanProfileName(source + " Kopie"));
        if (name == null || OverlayConfigFile.ReadProfile(_inst, source) is not { } json)
            return;
        var copy = OverlayProfile.Parse(json.GetRawText());
        copy.Root.Remove("server"); // Server gehören zum Original, sonst würden zwei Profile um sie streiten
        copy.Root.Remove("profil");
        OverlayConfigFile.SaveProfile(_inst, name, copy);
        Refresh();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        var name = NameOf(sender);
        if (!await _app.Dialogs.ConfirmAsync("Profil löschen", $"Das Profil \"{name}\" löschen?", "Löschen", danger: true))
            return;
        var wasActive = OverlayConfigFile.ActiveProfile(_inst).Equals(name, StringComparison.OrdinalIgnoreCase);
        OverlayConfigFile.DeleteProfile(_inst, name);
        if (wasActive)
            OverlayConfigFile.Activate(_inst, OverlayConfigFile.DefaultProfile);
        Refresh();
    }

    /// <summary>Server zuordnen: die Serverliste der Instanz zum Anhaken.</summary>
    private async void Servers_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        var name = NameOf(sender);
        if (OverlayConfigFile.ReadProfile(_inst, name) is not { } json)
            return;
        var profile = OverlayProfile.Parse(json.GetRawText());
        var chosen = profile.Servers;

        // Serverliste der Instanz; Adressen, die im Profil stehen, aber nicht (mehr) in der Liste, bleiben erhalten
        var options = new ServerStore(_inst.GameDir).Load()
            .Select(s => (s.Name, Address: s.Address.Trim()))
            .Where(s => s.Address.Length > 0)
            .DistinctBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var extra in chosen.Where(c => !options.Any(o => o.Address.Equals(c, StringComparison.OrdinalIgnoreCase))))
            options.Add((extra, extra));

        var form = new StackPanel();
        form.Children.Add(Ui.Note($"Auf diesen Servern schaltet sich \"{name}\" beim Betreten automatisch ein, beim " +
                                  "Verlassen geht es zurück.", 10));
        var boxes = new List<(CheckBox Box, string Address)>();
        var list = new StackPanel();
        foreach (var (serverName, address) in options)
        {
            var box = Ui.Check(serverName.Equals(address, StringComparison.OrdinalIgnoreCase) ? address : $"{serverName}  ·  {address}",
                chosen.Any(c => c.Equals(address, StringComparison.OrdinalIgnoreCase)));
            boxes.Add((box, address));
            list.Children.Add(box);
        }
        if (boxes.Count == 0)
            list.Children.Add(Ui.Note("Diese Instanz hat noch keine Server. Füge sie unter \"Server\" hinzu, dann kannst du sie hier anhaken.", 0, 11));
        form.Children.Add(Ui.Scroll(list, 260));

        if (!await _app.Dialogs.ShowFormAsync("Server für das Profil", form, "Speichern"))
            return;
        profile.Servers = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Address).ToList();
        OverlayConfigFile.SaveProfile(_inst, name, profile);
        Refresh();
    }

    /// <summary>Fragt nach einem freien Profilnamen; null bei Abbruch.</summary>
    private async Task<string?> AskNameAsync(string title, string suggestion)
    {
        var existing = OverlayConfigFile.ProfileNames(_inst!);
        var box = Ui.Input(suggestion);
        var problem = Ui.Problem();
        var form = new StackPanel();
        form.Children.Add(Ui.Label("Name des Profils"));
        form.Children.Add(box);
        form.Children.Add(problem);

        bool Validate()
        {
            var name = OverlayConfigFile.CleanProfileName(box.Text);
            string? reason = name.Length == 0 ? "Gib einen Namen ein (Buchstaben, Ziffern, Leerzeichen, - und _)."
                : existing.Contains(name, StringComparer.OrdinalIgnoreCase) ? $"Ein Profil \"{name}\" gibt es schon."
                : null;
            problem.Text = reason ?? "";
            problem.Visibility = reason == null || box.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            return reason == null;
        }

        box.TextChanged += (_, _) => Validate();
        Validate();
        return await _app.Dialogs.ShowFormAsync(title, form, "Anlegen", Validate)
            ? OverlayConfigFile.CleanProfileName(box.Text)
            : null;
    }
}

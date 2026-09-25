using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class OverlayPanel : UserControl
{
    public sealed record ProfileRow(string Name, bool IsActive, bool IsDefault, string Summary)
    {
        public bool CanActivate => !IsActive;
        public bool CanDelete => !IsDefault;
    }

    private AppServices _app = null!;
    private Installation? _inst;
    private OverlayStore _store = null!;

    public OverlayPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        _store = new OverlayStore(inst);
        _store.EnsureDefaultProfile();
        Refresh();
    }

    private bool Running => _inst != null && _app.Games.IsRunning(_inst);

    private void Refresh()
    {
        if (_inst == null)
            return;
        var active = _store.ActiveProfile();
        ProfileItems.ItemsSource = _store.ProfileNames().Select(name =>
        {
            var isDefault = name == OverlayStore.DefaultProfile;
            var servers = _store.LoadProfile(name)?.Servers ?? [];
            var summary = isDefault ? "Gilt überall, wo kein anderes Profil passt"
                : servers.Count > 0 ? "Automatisch auf: " + string.Join(", ", servers)
                : "Wechselt nur von Hand";
            return new ProfileRow(name, name.Equals(active, StringComparison.OrdinalIgnoreCase), isDefault, summary);
        }).ToList();
        Ui.Show(RunningNote, Running);
        ProfileItems.IsEnabled = !Running;
    }

    private static string NameOf(object sender) => Ui.DataOf<ProfileRow>(sender).Name;

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        _store.Activate(NameOf(sender));
        Refresh();
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (_inst != null)
            await ShareDialogs.ShareInstanceAsync(_app, _inst, overlayProfile: NameOf(sender));
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        var source = NameOf(sender);
        var existing = _store.ProfileNames();
        var name = await NamePrompt.AskAsync(_app, "Profil kopieren", OverlayStore.CleanProfileName(source + " Kopie"),
            "Anlegen", OverlayStore.CleanProfileName,
            clean => clean.Length == 0 ? "Gib einen Namen ein (Buchstaben, Ziffern, Leerzeichen, - und _)."
                : existing.Contains(clean, StringComparer.OrdinalIgnoreCase) ? $"Ein Profil \"{clean}\" gibt es schon."
                : null);
        if (name == null || _store.LoadProfile(source) is not { } profile)
            return;
        _store.WriteProfile(name, profile.Detached());
        Refresh();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        var name = NameOf(sender);
        if (!await _app.Dialogs.ConfirmAsync("Profil löschen", $"Das Profil \"{name}\" löschen?", "Löschen", danger: true))
            return;
        _store.DeleteProfile(name);
        Refresh();
    }

    private async void Servers_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || Running)
            return;
        var name = NameOf(sender);
        if (_store.LoadProfile(name) is not { } profile)
            return;

        var chosen = await ServerPicker.ChooseAsync(_app, _inst,
            $"Auf diesen Servern schaltet sich \"{name}\" beim Betreten automatisch ein, beim Verlassen geht es zurück.",
            "Diese Instanz hat noch keine Server. Füge sie unter \"Server\" hinzu, dann kannst du sie hier anhaken.",
            profile.Servers);
        if (chosen == null)
            return;
        profile.Servers = chosen;
        _store.WriteProfile(name, profile);
        Refresh();
    }
}

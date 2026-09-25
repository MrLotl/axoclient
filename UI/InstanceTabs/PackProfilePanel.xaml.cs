using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class PackProfilePanel : UserControl
{
    private AppServices _app = null!;
    private Installation? _inst;
    private PackProfileStore _store = null!;
    private List<PackProfile> _profiles = [];

    public PackProfilePanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        _store = new PackProfileStore(inst);
        StatusText.Text = "";
        Refresh();
    }

    private bool Running => _inst != null && _app.Games.IsRunning(_inst);

    private void Refresh()
    {
        if (_inst == null)
            return;
        _profiles = _store.Load();
        ProfileItems.ItemsSource = null;
        ProfileItems.ItemsSource = _profiles;
        Header.Text = $"Paket-Profile ({_profiles.Count})";

        var packs = _store.CurrentResourcePacks().Count(p => !p.Equals("vanilla", StringComparison.OrdinalIgnoreCase));
        SubHeader.Text = "Gerade aktiv: " + Formats.Count(packs, "Ressourcenpaket", "Ressourcenpakete") +
                         _store.CurrentShader() switch
                         {
                             null => "",
                             "" => ", kein Shader",
                             var shader => $", Shader {Path.GetFileNameWithoutExtension(shader)}"
                         };

        Ui.Show(EmptyText, _profiles.Count == 0);
        Ui.Show(RunningNote, Running);
        ProfileItems.IsEnabled = !Running;
    }

    private void Save(string status)
    {
        _store.Save(_profiles);
        StatusText.Text = status;
        Refresh();
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || !await CheckNotRunningAsync())
            return;
        var name = await AskNameAsync("Profil aufnehmen", SuggestName(), null);
        if (name == null)
            return;
        var profile = _store.Capture(name);
        _profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _profiles.Add(profile);
        Save($"Profil \"{name}\" aufgenommen: {profile.Summary}");
    }

    private string SuggestName() =>
        Enumerable.Range(1, PackProfileStore.MaxProfiles)
            .Select(n => n == 1 ? "Profil" : $"Profil {n}")
            .FirstOrDefault(candidate => !_profiles.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        ?? "Profil";

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || !await CheckNotRunningAsync())
            return;
        var profile = Ui.DataOf<PackProfile>(sender);
        StatusText.Text = $"\"{profile.Name}\": " + string.Join(" · ", _store.Apply(profile));
        Refresh();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null || !await CheckNotRunningAsync())
            return;
        var profile = Ui.DataOf<PackProfile>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Profil überschreiben",
                $"\"{profile.Name}\" mit den gerade aktiven Paketen und dem eingestellten Shader überschreiben?", "Überschreiben"))
            return;

        var fresh = _store.Capture(profile.Name);
        fresh.Servers = profile.Servers;
        _profiles[_profiles.IndexOf(profile)] = fresh;
        Save($"\"{profile.Name}\" überschrieben: {fresh.Summary}");
    }

    private async void Servers_Click(object sender, RoutedEventArgs e)
    {
        if (_inst is not { } inst)
            return;
        var profile = Ui.DataOf<PackProfile>(sender);
        var owners = _profiles.Where(p => p != profile)
            .SelectMany(p => p.Servers.Select(s => (Address: s, p.Name)))
            .DistinctBy(t => t.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.Address, t => t.Name, StringComparer.OrdinalIgnoreCase);

        var chosen = await ServerPicker.ChooseAsync(_app, inst,
            $"Startest du über „Server“ oder die Freundesliste auf einen dieser Server, setzt der Launcher vorher \"{profile.Name}\".",
            "Diese Instanz hat noch keine Server. Füge sie unter „Server“ hinzu.",
            profile.Servers, address => owners.GetValueOrDefault(address));
        if (chosen == null)
            return;
        profile.Servers = chosen;
        Save("");
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var profile = Ui.DataOf<PackProfile>(sender);
        if (await AskNameAsync("Profil umbenennen", profile.Name, profile) is not { } name)
            return;
        profile.Name = name;
        Save("");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var profile = Ui.DataOf<PackProfile>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Profil löschen",
                $"Das Profil \"{profile.Name}\" löschen? Die Pakete selbst bleiben erhalten.", "Löschen", danger: true))
            return;
        _profiles.Remove(profile);
        Save($"\"{profile.Name}\" gelöscht.");
    }

    private async Task<bool> CheckNotRunningAsync()
    {
        if (!Running)
            return true;
        await _app.Dialogs.ShowMessageAsync("Minecraft läuft",
            $"\"{_inst!.Name}\" läuft gerade. Das Spiel schreibt seine Einstellungen beim Beenden neu und würde " +
            "die Änderung überschreiben. Beende es zuerst.");
        return false;
    }

    private Task<string?> AskNameAsync(string title, string suggestion, PackProfile? self) =>
        NamePrompt.AskAsync(_app, title, suggestion, "Speichern", PackProfileStore.CleanName,
            name => name.Length == 0 ? "Gib einen Namen ein."
                : _profiles.Any(p => p != self && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ? $"Ein Profil \"{name}\" gibt es schon."
                    : _profiles.Count >= PackProfileStore.MaxProfiles && self == null
                        ? $"Mehr als {PackProfileStore.MaxProfiles} Profile gehen nicht."
                        : null);
}

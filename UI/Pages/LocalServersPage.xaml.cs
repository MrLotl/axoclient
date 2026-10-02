using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Pages;

public sealed class ServerCardItem : ISpecialItem
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public ServerCardItem(LocalServer server, RunningServer? state, string? busy, bool copied)
    {
        Server = server;
        State = state;
        Busy = busy ?? "";
        Name = server.Name;
        Type = server.TypeLabel;
        Address = server.Address;
        AddressTitle = copied ? "Kopiert!" : "Adresse";
        Icon = File.Exists(server.IconFile) ? Images.FromFile(server.IconFile) : null;
        Placeholder = Letters.BrushFor(server.Name);
        var ram = server.RamMb / 1024.0;
        if (state != null)
        {
            var used = state.RamBytes / 1073741824.0;
            Players = state.Reachable ? $"{state.Online} / {state.Max}" : "startet …";
            RamLabel = $"RAM {used.ToString("0.0", German)} / {ram.ToString("0.#", German)} GB";
            RamFraction = Math.Clamp(used / Math.Max(ram, 0.1), 0, 1);
            var up = DateTime.UtcNow - state.StartedUtc;
            Uptime = up.TotalMinutes < 1 ? "gerade gestartet"
                : up.TotalHours >= 1 ? $"{(int)up.TotalHours} Std {up.Minutes} Min" : $"{up.Minutes} Min";
        }
        else
        {
            Players = "–";
            RamLabel = $"RAM · {ram.ToString("0.#", German)} GB";
            RamFraction = 0;
            Uptime = "–";
        }
    }

    public LocalServer? Server { get; }
    public RunningServer? State { get; }
    public bool IsSpecial => Server == null;
    public string Name { get; } = "";
    public string Type { get; } = "";
    public string Address { get; } = "";
    public string AddressTitle { get; } = "";
    public BitmapSource? Icon { get; }
    public Brush? Placeholder { get; }
    public string Players { get; } = "";
    public string RamLabel { get; } = "";
    public double RamFraction { get; }
    public string Uptime { get; } = "";
    public string Busy { get; } = "";
    public bool HasBusy => Busy.Length > 0;
    public bool Running => State != null;
    public bool Stopping => State?.Stopping == true;
    public bool CanToggle => !HasBusy && !Stopping;

    public string StatusLabel => Stopping ? "Stoppt …" : Running ? "Läuft" : HasBusy ? "Startet …" : "Gestoppt";
    public Brush StatusBackground => Running ? Ui.Frozen(Color.FromArgb(0x24, 0x3F, 0xB9, 0x50)) : Ui.Frozen(Color.FromRgb(0x33, 0x33, 0x33));
    public Brush StatusForeground => Running ? Ui.Frozen(Color.FromRgb(0x6F, 0xDC, 0x80)) : Ui.Resource<Brush>("TextSecondary");
    public Brush StatusDot => Running ? Ui.Resource<Brush>("Good") : Ui.Resource<Brush>("Offline");
    public string RunLabel => Running ? "Stoppen" : "Starten";
    public string RunIcon => Running ? "Stop" : "Play";
    public Brush RunBackground => Running ? Ui.Frozen(Color.FromRgb(0x3D, 0x26, 0x26)) : Ui.Resource<Brush>("GoodBg");
    public Brush RunForeground => Running ? Ui.Resource<Brush>("DangerText") : Ui.Resource<Brush>("Good");

    public static ServerCardItem CreateCard() => new();

    private ServerCardItem()
    {
    }
}

public partial class LocalServersPage : UserControl
{
    private readonly Dictionary<string, string> _busy = new();
    private AppServices _app = null!;
    private string? _copied;

    public LocalServersPage()
    {
        InitializeComponent();
    }

    public event Action<string, string?>? JoinRequested;
    public event Action<LocalServer>? ConsoleRequested;

    private ServerHost Host => _app.LocalServers;

    public void Initialize(AppServices app)
    {
        _app = app;
        Host.Changed += () => Dispatcher.InvokeAsync(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var items = Host.Servers
            .Select(s => new ServerCardItem(s, Host.StateOf(s), _busy.GetValueOrDefault(s.Id), _copied == s.Id))
            .Append(ServerCardItem.CreateCard())
            .ToList();
        ServerList.ItemsSource = items;
        var running = Host.Servers.Count(Host.IsRunning);
        SummaryText.Text = $"{Formats.Count(Host.Servers.Count, "Server", "Server")} auf diesem PC · " +
                           (running == 0 ? "keiner läuft" : running == 1 ? "1 läuft" : $"{running} laufen");
    }

    private static LocalServer Of(object sender) => Ui.DataOf<ServerCardItem>(sender).Server!;

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (await LocalServerDialog.ShowAsync(_app, null) is { } created)
            await StartAsync(created);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e) => await LocalServerDialog.ShowAsync(_app, Of(sender));

    private async void Delete_Click(object sender, RoutedEventArgs e) => await LocalServerDialog.DeleteAsync(_app, Of(sender));

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var server = Of(sender);
        if (Host.IsRunning(server))
            await Host.StopAsync(server);
        else
            await StartAsync(server);
    }

    private async Task StartAsync(LocalServer server)
    {
        if (_busy.ContainsKey(server.Id))
            return;
        _busy[server.Id] = "Bereite vor …";
        Refresh();
        try
        {
            var progress = new WorkProgress(new Progress<string>(text =>
            {
                _busy[server.Id] = text;
                Refresh();
            }), new Progress<double>(), CancellationToken.None);
            await Host.StartAsync(server, progress);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync($"„{server.Name}“ konnte nicht gestartet werden", ex);
        }
        finally
        {
            _busy.Remove(server.Id);
            Refresh();
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var server = Of(sender);
        try
        {
            Clipboard.SetText(server.Address);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Adresse kopieren", ex);
            return;
        }
        _copied = server.Id;
        Refresh();
        await Task.Delay(1400);
        _copied = null;
        Refresh();
    }

    private void Join_Click(object sender, RoutedEventArgs e)
    {
        var server = Of(sender);
        JoinRequested?.Invoke(server.Address, server.IsProxy || server.Version.Length == 0 ? null : server.Version);
    }

    private void Console_Click(object sender, RoutedEventArgs e) => ConsoleRequested?.Invoke(Of(sender));

    private void Folder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(Of(sender).Dir, create: true);

    private void OpenRoot_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(AppPaths.LocalServers, create: true);

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Server-Ordner auswählen" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        await UiRun.GuardAsync(_app, "Server konnte nicht übernommen werden", () =>
        {
            Host.Import(dialog.FolderName);
            return Task.CompletedTask;
        });
    }
}

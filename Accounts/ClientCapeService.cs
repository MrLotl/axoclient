using System.Text.Json;
using System.Text.RegularExpressions;

namespace AxoClient.Accounts;

public record ClientCape(string Id, string Name, byte[]? Png);

public sealed partial class ClientCapeService
{
    private const string RunningGameFile = "axoclient-cape.txt";

    private readonly HttpClient _http;
    private readonly AccountService _accounts;
    private readonly AxoService _axo;
    private readonly Func<IEnumerable<Installation>> _runningInstances;

    public ClientCapeService(HttpClient http, AccountService accounts, AxoService axo,
        Func<IEnumerable<Installation>> runningInstances)
    {
        _http = http;
        _accounts = accounts;
        _axo = axo;
        _runningInstances = runningInstances;
        accounts.SignedIn += () => _ = RefreshAsync();
        accounts.SignedOut += () => SelectedId = null;
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex ValidId();

    public List<ClientCape> All { get; private set; } = [];

    public string? SelectedId { get; private set; }

    public ClientCape? Selected => All.FirstOrDefault(c => c.Id == SelectedId);

    public byte[]? DisplayPng => Selected?.Png ?? _accounts.Profile?.ActiveCapePng;

    public async Task RefreshAsync()
    {
        try
        {
            All = await LoadAsync();
            _accounts.ReportProblem("", null);
        }
        catch (Exception ex)
        {
            _accounts.ReportProblem("Die Umhänge konnten nicht geladen werden", ex);
        }
        try
        {
            SelectedId = _axo.Available ? await _axo.GetCapeAsync() : null;
        }
        catch (Exception ex)
        {
            _accounts.ReportProblem("Dein gewählter Umhang konnte nicht abgefragt werden", ex);
        }
        _accounts.NotifyChanged();
    }

    public async Task SelectAsync(string? capeId)
    {
        await _axo.SetCapeAsync(capeId);
        SelectedId = capeId;
        TellRunningGames(capeId);
        _accounts.NotifyChanged();
    }

    private async Task<List<ClientCape>> LoadAsync()
    {
        using var json = JsonDocument.Parse(await _http.GetStringAsync(AppInfo.ClientCapesUrl + "capes.json"));
        var entries = json.RootElement.EnumerateArray()
            .Select(e => (Id: e.GetProperty("id").GetString() ?? "", Name: JsonFiles.String(e, "name")))
            .Where(e => ValidId().IsMatch(e.Id))
            .ToList();

        return (await Task.WhenAll(entries.Select(async e =>
        {
            byte[]? png = null;
            try
            {
                png = await _http.GetByteArrayAsync(AppInfo.ClientCapesUrl + e.Id + ".png");
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Vorschau eines AxoClient-Umhangs laden", ex);
            }
            return new ClientCape(e.Id, e.Name ?? e.Id, png);
        }))).ToList();
    }

    private void TellRunningGames(string? capeId)
    {
        if (_accounts.CompactUuid is not { Length: > 0 } uuid)
            return;
        foreach (var inst in _runningInstances())
        {
            try
            {
                File.WriteAllText(inst.GameFile(RunningGameFile), uuid + "\n" + (capeId ?? ""));
            }
            catch (Exception ex)
            {
                ErrorReport.Log($"Umhang an laufendes Spiel \"{inst.Name}\" melden", ex);
            }
        }
    }
}

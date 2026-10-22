using AxoClient.Core.Common;

namespace AxoClient.Core.Accounts;

/// <summary>
/// Verwaltet die Accounts aus <c>accounts.json</c> und hält die aktuelle Sitzung im Speicher.
/// Läuft das Erneuern still, merkt der Nutzer nichts; schlägt es fehl, muss er sich neu anmelden.
/// </summary>
public sealed class AccountManager
{
    private readonly JsonStore<AccountsDocument> _store;
    private readonly Dictionary<AccountType, IAuthProvider> _providers;
    private readonly EventHub _events;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private AccountsDocument _doc = new();
    private GameSession? _session;
    private DateTimeOffset _sessionAt;

    public AccountManager(AppPaths paths, IEnumerable<IAuthProvider> providers, EventHub events)
    {
        _store = new JsonStore<AccountsDocument>(paths.Fs, paths.AccountsJson);
        _providers = providers.ToDictionary(p => p.Type);
        _events = events;
    }

    public IReadOnlyList<AccountEntry> Accounts => _doc.Accounts;
    public AccountEntry? Active => _doc.Accounts.FirstOrDefault(a => a.Uuid == _doc.Active);

    /// <summary>Ist die aktive Anmeldung kaputt (z. B. abgelaufen), damit die UI "Willkommen zurück" zeigt.</summary>
    public bool ActiveNeedsLogin { get; private set; }

    public event Action? Changed;

    public async Task LoadAsync()
    {
        _doc = await _store.LoadAsync();
        foreach (var account in _doc.Accounts)
            LogRedactor.RegisterPlayerName(account.Name);
        if (_doc.Active == null || Active == null)
            _doc.Active = _doc.Accounts.FirstOrDefault()?.Uuid;
        ActiveNeedsLogin = Active is { } a && !Provider(a.Type).HasStoredLogin(a.Uuid);
    }

    public IReadOnlyList<AccountView> Views() => _doc.Accounts.Select(a => new AccountView(
        a.Uuid, a.Name, a.Type, a.Uuid == _doc.Active, $"/axo/head/{a.Uuid.Replace("-", "")}",
        a.Uuid == _doc.Active ? ActiveNeedsLogin : !Provider(a.Type).HasStoredLogin(a.Uuid))).ToList();

    /// <summary>Neues Konto hinzufügen (oder ein bestehendes neu anmelden) und aktiv setzen.</summary>
    public async Task<AccountEntry> LoginAsync(AccountType type, CancellationToken ct)
    {
        var session = await Provider(type).LoginAsync(ct);
        await _sessionLock.WaitAsync(ct);
        try
        {
            _doc = await _store.UpdateAsync(doc =>
            {
                var existing = doc.Accounts.FirstOrDefault(a => a.Uuid == session.Uuid);
                if (existing == null)
                    doc.Accounts.Add(new AccountEntry { Uuid = session.Uuid, Name = session.Name, Type = type });
                else
                    existing.Name = session.Name;
                doc.Active = session.Uuid;
            }, ct);
            SetSession(session);
            ActiveNeedsLogin = false;
        }
        finally
        {
            _sessionLock.Release();
        }
        LogRedactor.RegisterPlayerName(session.Name);
        RaiseChanged();
        return Active!;
    }

    public async Task SwitchAsync(string uuid, CancellationToken ct)
    {
        if (_doc.Accounts.All(a => a.Uuid != uuid))
            throw new AxoException("errors.notFound");
        _doc = await _store.UpdateAsync(doc => doc.Active = uuid, ct);
        _session = null;
        ActiveNeedsLogin = false;
        try
        {
            await GetSessionAsync(ct);
        }
        catch (AxoException ex) when (ex.Code == "auth.expired")
        {
            ActiveNeedsLogin = true;
        }
        RaiseChanged();
    }

    public async Task RemoveAsync(string uuid, CancellationToken ct)
    {
        var entry = _doc.Accounts.FirstOrDefault(a => a.Uuid == uuid);
        if (entry == null)
            return;
        await Provider(entry.Type).SignOutAsync(uuid, ct);
        _doc = await _store.UpdateAsync(doc =>
        {
            doc.Accounts.RemoveAll(a => a.Uuid == uuid);
            if (doc.Active == uuid)
                doc.Active = doc.Accounts.FirstOrDefault()?.Uuid;
        }, ct);
        if (_session?.Uuid == uuid)
            _session = null;
        RaiseChanged();
    }

    /// <summary>Gültige Sitzung für den Spielstart. Erneuert still, wenn sie älter als 10 Minuten ist.</summary>
    public async Task<GameSession> GetSessionAsync(CancellationToken ct, bool forceRefresh = false)
    {
        var active = Active ?? throw new AxoException("auth.noAccount");
        await _sessionLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _session?.Uuid == active.Uuid && DateTimeOffset.UtcNow - _sessionAt < TimeSpan.FromMinutes(10))
                return _session;
            try
            {
                var session = await Provider(active.Type).RefreshAsync(active.Uuid, ct);
                SetSession(session);
                if (session.Name != active.Name)
                {
                    _doc = await _store.UpdateAsync(doc =>
                    {
                        if (doc.Accounts.FirstOrDefault(a => a.Uuid == session.Uuid) is { } e)
                            e.Name = session.Name;
                    }, ct);
                    RaiseChanged();
                }
                if (ActiveNeedsLogin)
                {
                    ActiveNeedsLogin = false;
                    RaiseChanged();
                }
                return session;
            }
            catch (AxoException ex) when (ex.Code == "auth.expired")
            {
                ActiveNeedsLogin = true;
                _events.Publish("notify", new { type = "authExpired", text = new TextRef("notifications.authExpired") });
                RaiseChanged();
                throw;
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>Für Skin/Cape-Aufrufe an Mojang, ohne das Token herauszugeben.</summary>
    public GameSession? CachedSession => _session;

    public async Task UpdateEntryAsync(string uuid, Action<AccountEntry> change)
    {
        _doc = await _store.UpdateAsync(doc =>
        {
            if (doc.Accounts.FirstOrDefault(a => a.Uuid == uuid) is { } e)
                change(e);
        });
        RaiseChanged();
    }

    private void SetSession(GameSession session)
    {
        _session = session;
        _sessionAt = DateTimeOffset.UtcNow;
    }

    private IAuthProvider Provider(AccountType type) =>
        _providers.TryGetValue(type, out var p) ? p : throw new AxoException("auth.failed");

    private void RaiseChanged()
    {
        Changed?.Invoke();
        _events.Publish("accounts.changed", Views());
    }
}

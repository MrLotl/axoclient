using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;

namespace AxoClient.Accounts
{
    /// <summary>
    /// Verwaltet die Accounts aus <c>accounts.json</c> und hält die aktuelle Sitzung im Speicher.
    /// Läuft das Erneuern still, merkt der Nutzer nichts; schlägt es fehl, muss er sich neu anmelden.
    /// </summary>
    public sealed class AccountManager : IAccountManager
    {
        private readonly ILogRedactor _logRedactor;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly JsonStore<AccountsDocument> _store;
        private readonly Dictionary<AccountType, IAuthProvider> _providers;
        private readonly IEventHub _events;
        private readonly SemaphoreSlim _sessionLock = new(1, 1);
        private AccountsDocument _doc = new();
        private GameSession? _session;
        private DateTimeOffset _sessionAt;

        public AccountManager(
            AppPaths paths,
            IEnumerable<IAuthProvider> providers,
            IEventHub events,
            ILogRedactor logRedactor,
            IJsonStoreFactory jsonStoreFactory)
        {
            _logRedactor = logRedactor;
            _jsonStoreFactory = jsonStoreFactory;
            _store = _jsonStoreFactory.Create<AccountsDocument>(paths.Fs, paths.AccountsJson);
            _providers = providers.ToDictionary(authProvider => authProvider.Type);
            _events = events;
        }

        public IReadOnlyList<AccountEntry> Accounts => _doc.Accounts;
        public AccountEntry? Active => _doc.Accounts.FirstOrDefault(account => account.Uuid == _doc.Active);

        /// <summary>Ist die aktive Anmeldung kaputt (z. B. abgelaufen), damit die UI "Willkommen zurück" zeigt.</summary>
        public bool ActiveNeedsLogin { get; private set; }

        public event Action? Changed;

        public async Task Load()
        {
            _doc = await _store.Load();
            foreach (var account in _doc.Accounts)
            {
                _logRedactor.RegisterPlayerName(account.Name);
            }

            if (_doc.Active == null || Active == null)
            {
                _doc.Active = _doc.Accounts.FirstOrDefault()?.Uuid;
            }

            ActiveNeedsLogin = Active is { } activeAccount && !provider(activeAccount.Type).HasStoredLogin(activeAccount.Uuid);
        }

        public IReadOnlyList<AccountView> Views() => _doc.Accounts.Select(account => new AccountView(
            account.Uuid, account.Name, account.Type, account.Uuid == _doc.Active, $"/axo/head/{account.Uuid.Replace("-", "")}",
            account.Uuid == _doc.Active ? ActiveNeedsLogin : !provider(account.Type).HasStoredLogin(account.Uuid))).ToList();

        /// <summary>Neues Konto hinzufügen (oder ein bestehendes neu anmelden) und aktiv setzen.</summary>
        public async Task<AccountEntry> Login(AccountType type, CancellationToken cancellationToken)
        {
            var session = await provider(type).Login(cancellationToken);
            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                _doc = await _store.Update(document =>
                {
                    var existing = document.Accounts.FirstOrDefault(account => account.Uuid == session.Uuid);
                    if (existing == null)
                    {
                        document.Accounts.Add(new AccountEntry { Uuid = session.Uuid, Name = session.Name, Type = type });
                    }
                    else
                    {
                        existing.Name = session.Name;
                    }

                    document.Active = session.Uuid;
                }, cancellationToken);
                setSession(session);
                ActiveNeedsLogin = false;
            }
            finally
            {
                _sessionLock.Release();
            }
            _logRedactor.RegisterPlayerName(session.Name);
            raiseChanged();
            return Active!;
        }

        public async Task Switch(string uuid, CancellationToken cancellationToken)
        {
            if (_doc.Accounts.All(account => account.Uuid != uuid))
            {
                throw new AxoException("errors.notFound");
            }

            _doc = await _store.Update(document => document.Active = uuid, cancellationToken);
            _session = null;
            ActiveNeedsLogin = false;
            try
            {
                await GetSession(cancellationToken);
            }
            catch (AxoException exception) when (exception.Code == "auth.expired")
            {
                ActiveNeedsLogin = true;
            }
            raiseChanged();
        }

        public async Task Remove(string uuid, CancellationToken cancellationToken)
        {
            var entry = _doc.Accounts.FirstOrDefault(account => account.Uuid == uuid);
            if (entry == null)
            {
                return;
            }

            await provider(entry.Type).SignOut(uuid, cancellationToken);
            _doc = await _store.Update(document =>
            {
                document.Accounts.RemoveAll(account => account.Uuid == uuid);
                if (document.Active == uuid)
                {
                    document.Active = document.Accounts.FirstOrDefault()?.Uuid;
                }
            }, cancellationToken);
            if (_session?.Uuid == uuid)
            {
                _session = null;
            }

            raiseChanged();
        }

        /// <summary>Gültige Sitzung für den Spielstart. Erneuert still, wenn sie älter als 10 Minuten ist.</summary>
        public async Task<GameSession> GetSession(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            var active = Active ?? throw new AxoException("auth.noAccount");
            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && _session?.Uuid == active.Uuid && DateTimeOffset.UtcNow - _sessionAt < TimeSpan.FromMinutes(10))
                {
                    return _session;
                }

                try
                {
                    var session = await provider(active.Type).Refresh(active.Uuid, cancellationToken);
                    setSession(session);
                    if (session.Name != active.Name)
                    {
                        _doc = await _store.Update(document =>
                        {
                            if (document.Accounts.FirstOrDefault(account => account.Uuid == session.Uuid) is { } existingAccount)
                            {
                                existingAccount.Name = session.Name;
                            }
                        }, cancellationToken);
                        raiseChanged();
                    }
                    if (ActiveNeedsLogin)
                    {
                        ActiveNeedsLogin = false;
                        raiseChanged();
                    }
                    return session;
                }
                catch (AxoException exception) when (exception.Code == "auth.expired")
                {
                    ActiveNeedsLogin = true;
                    _events.Publish("notify", new { type = "authExpired", text = new TextRef("notifications.authExpired") });
                    raiseChanged();
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

        public async Task UpdateEntry(string uuid, Action<AccountEntry> change)
        {
            _doc = await _store.Update(document =>
            {
                if (document.Accounts.FirstOrDefault(account => account.Uuid == uuid) is { } existingAccount)
                {
                    change(existingAccount);
                }
            });
            raiseChanged();
        }

        private void setSession(GameSession session)
        {
            _session = session;
            _sessionAt = DateTimeOffset.UtcNow;
        }

        private IAuthProvider provider(AccountType type) =>
            _providers.TryGetValue(type, out var value) ? value : throw new AxoException("auth.failed");

        private void raiseChanged()
        {
            Changed?.Invoke();
            _events.Publish("accounts.changed", Views());
        }
    }
}

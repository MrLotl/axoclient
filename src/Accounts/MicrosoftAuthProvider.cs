using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.DataModel.Accounts;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using XboxAuthNet.Game.Accounts;

namespace AxoClient.Accounts
{
    /// <summary>
    /// Microsoft-Anmeldung über CmlLib. Phase 1 nutzt die Standard-Client-ID von CmlLib (<c>BuildDefault</c>).
    /// Für den Wechsel auf die eigene Azure-App muss nur <see cref="ClientId"/> gesetzt werden.
    /// </summary>
    public sealed class MicrosoftAuthProvider : IAuthProvider
    {
        private readonly IAppLog _appLog;
        private readonly IUuidHelper _uuidHelper;
        /// <summary>Eigene Client-ID nach Freigabe durch Microsoft eintragen. <c>null</c> = CmlLib-Standard.</summary>
        public const string? ClientId = null;

        private const string StorageKey = "cmllib.accounts";

        private readonly JELoginHandler _login;

        public MicrosoftAuthProvider(HttpClient http, ITokenStore tokens, IAppLog appLog, IUuidHelper uuidHelper)
        {
            _appLog = appLog;
            _uuidHelper = uuidHelper;
            var manager = new JsonXboxGameAccountManager(new TokenStoreJsonStorage(tokens, StorageKey),
                JEGameAccount.FromSessionStorage, xboxGameAccountManagerDefaults());
            var builder = new JELoginHandlerBuilder()
                .WithHttpClient(http)
                .WithAccountManager(manager);
            _login = builder.Build();
        }

        public AccountType Type => AccountType.Microsoft;

        public async Task<GameSession> Login(CancellationToken ct)
        {
            var account = _login.AccountManager.NewAccount();
            try
            {
                // Das Anmeldefenster von CmlLib braucht einen STA-Thread mit eigener Nachrichtenschleife
                var session = await runOnStaThread(() => _login.AuthenticateInteractively(account, ct));
                return toSession(session);
            }
            catch (Exception ex) when (ex is not AxoException)
            {
                throw translate(ex);
            }
        }

        public async Task<GameSession> Refresh(string uuid, CancellationToken ct)
        {
            var account = find(uuid) ?? throw new AxoException("auth.expired");
            try
            {
                return toSession(await _login.AuthenticateSilently(account, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not AxoException)
            {
                _appLog.Warn("Stilles Erneuern der Anmeldung fehlgeschlagen", ex);
                throw ex is HttpRequestException { StatusCode: null } ? new AxoException("errors.offline") : new AxoException("auth.expired");
            }
        }

        public async Task SignOut(string uuid, CancellationToken ct)
        {
            if (find(uuid) is { } account)
            {
                await _login.Signout(account, ct);
            }
        }

        public bool HasStoredLogin(string uuid) => find(uuid) != null;

        private IXboxGameAccount? find(string uuid)
        {
            var compact = uuid.Replace("-", "");
            return _login.AccountManager.GetAccounts().OfType<JEGameAccount>()
                .FirstOrDefault(a => a.Profile?.UUID?.Replace("-", "").Equals(compact, StringComparison.OrdinalIgnoreCase) == true);
        }

        private GameSession toSession(MSession session)
        {
            if (string.IsNullOrEmpty(session.UUID) || string.IsNullOrEmpty(session.Username) || string.IsNullOrEmpty(session.AccessToken))
            {
                throw new AxoException("auth.noMinecraft");
            }

            return new GameSession(_uuidHelper.FormatUuid(session.UUID), session.Username, session.AccessToken, session.Xuid, AccountType.Microsoft);
        }

        private AxoException translate(Exception ex)
        {
            var message = ex.ToString();
            if (ex is OperationCanceledException || message.Contains("user_canceled", StringComparison.OrdinalIgnoreCase)
                                                 || message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
            {
                return new AxoException("auth.cancelled");
            }

            if (message.Contains("403") || message.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
            {
                return new AxoException("auth.appNotApproved");
            }

            if (message.Contains("NOT_FOUND") || message.Contains("does not own", StringComparison.OrdinalIgnoreCase)
                                              || message.Contains("2148916233") || message.Contains("404"))
            {
                return new AxoException("auth.noMinecraft");
            }

            if (ex is HttpRequestException { StatusCode: null })
            {
                return new AxoException("errors.offline");
            }

            _appLog.Error("Microsoft-Anmeldung fehlgeschlagen", ex);
            return new AxoException("auth.failed", inner: ex);
        }

        private System.Text.Json.JsonSerializerOptions xboxGameAccountManagerDefaults() => new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private Task<T> runOnStaThread<T>(Func<Task<T>> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    // Eigener Synchronisationskontext, damit das Anmeldefenster auf diesem Thread bleibt
                    var context = new StaSynchronizationContext();
                    SynchronizationContext.SetSynchronizationContext(context);
                    var task = work();
                    _ = task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
                    context.RunUntilComplete();
#pragma warning disable VSTHRD002 // Der Task ist hier bereits abgeschlossen
                    tcs.TrySetResult(task.GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            { IsBackground = true, Name = "MicrosoftLogin" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        /// <summary>Minimale Nachrichtenschleife für den Anmelde-Thread.</summary>
        private sealed class StaSynchronizationContext : SynchronizationContext
        {
            private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

            public override void Post(SendOrPostCallback d, object? state) => _queue.TryAdd((d, state));

            public override void Send(SendOrPostCallback d, object? state) => d(state);

            public void Complete() => _queue.CompleteAdding();

            public void RunUntilComplete()
            {
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            }
        }
    }
}

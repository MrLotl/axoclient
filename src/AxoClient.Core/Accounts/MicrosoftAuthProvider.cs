using AxoClient.Core.Common;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using XboxAuthNet.Game.Accounts;

namespace AxoClient.Core.Accounts;

/// <summary>
/// Microsoft-Anmeldung über CmlLib. Phase 1 nutzt die Standard-Client-ID von CmlLib (<c>BuildDefault</c>).
/// Für den Wechsel auf die eigene Azure-App muss nur <see cref="ClientId"/> gesetzt werden.
/// </summary>
public sealed class MicrosoftAuthProvider : IAuthProvider
{
    /// <summary>Eigene Client-ID nach Freigabe durch Microsoft eintragen. <c>null</c> = CmlLib-Standard.</summary>
    public const string? ClientId = null;

    private const string StorageKey = "cmllib.accounts";

    private readonly JELoginHandler _login;

    public MicrosoftAuthProvider(HttpClient http, TokenStore tokens)
    {
        var manager = new JsonXboxGameAccountManager(new TokenStoreJsonStorage(tokens, StorageKey),
            JEGameAccount.FromSessionStorage, XboxGameAccountManagerDefaults());
        var builder = new JELoginHandlerBuilder()
            .WithHttpClient(http)
            .WithAccountManager(manager);
        _login = builder.Build();
    }

    public AccountType Type => AccountType.Microsoft;

    public async Task<GameSession> LoginAsync(CancellationToken ct)
    {
        var account = _login.AccountManager.NewAccount();
        try
        {
            // Das Anmeldefenster von CmlLib braucht einen STA-Thread mit eigener Nachrichtenschleife
            var session = await RunOnStaThreadAsync(() => _login.AuthenticateInteractively(account, ct));
            return ToSession(session);
        }
        catch (Exception ex) when (ex is not AxoException)
        {
            throw Translate(ex);
        }
    }

    public async Task<GameSession> RefreshAsync(string uuid, CancellationToken ct)
    {
        var account = Find(uuid) ?? throw new AxoException("auth.expired");
        try
        {
            return ToSession(await _login.AuthenticateSilently(account, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AxoException)
        {
            AppLog.Warn("Stilles Erneuern der Anmeldung fehlgeschlagen", ex);
            throw ex is HttpRequestException { StatusCode: null } ? new AxoException("errors.offline") : new AxoException("auth.expired");
        }
    }

    public async Task SignOutAsync(string uuid, CancellationToken ct)
    {
        if (Find(uuid) is { } account)
            await _login.Signout(account, ct);
    }

    public bool HasStoredLogin(string uuid) => Find(uuid) != null;

    private IXboxGameAccount? Find(string uuid)
    {
        var compact = uuid.Replace("-", "");
        return _login.AccountManager.GetAccounts().OfType<JEGameAccount>()
            .FirstOrDefault(a => a.Profile?.UUID?.Replace("-", "").Equals(compact, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static GameSession ToSession(MSession session)
    {
        if (string.IsNullOrEmpty(session.UUID) || string.IsNullOrEmpty(session.Username) || string.IsNullOrEmpty(session.AccessToken))
            throw new AxoException("auth.noMinecraft");
        return new GameSession(FormatUuid(session.UUID), session.Username, session.AccessToken, session.Xuid, AccountType.Microsoft);
    }

    public static string FormatUuid(string uuid)
    {
        var c = uuid.Replace("-", "").ToLowerInvariant();
        return c.Length == 32 ? $"{c[..8]}-{c[8..12]}-{c[12..16]}-{c[16..20]}-{c[20..]}" : uuid;
    }

    private static AxoException Translate(Exception ex)
    {
        var message = ex.ToString();
        if (ex is OperationCanceledException || message.Contains("user_canceled", StringComparison.OrdinalIgnoreCase)
                                             || message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
            return new AxoException("auth.cancelled");
        if (message.Contains("403") || message.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
            return new AxoException("auth.appNotApproved");
        if (message.Contains("NOT_FOUND") || message.Contains("does not own", StringComparison.OrdinalIgnoreCase)
                                          || message.Contains("2148916233") || message.Contains("404"))
            return new AxoException("auth.noMinecraft");
        if (ex is HttpRequestException { StatusCode: null })
            return new AxoException("errors.offline");
        AppLog.Error("Microsoft-Anmeldung fehlgeschlagen", ex);
        return new AxoException("auth.failed", inner: ex);
    }

    private static System.Text.Json.JsonSerializerOptions XboxGameAccountManagerDefaults() => new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static Task<T> RunOnStaThreadAsync<T>(Func<Task<T>> work)
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
        }) { IsBackground = true, Name = "MicrosoftLogin" };
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
                callback(state);
        }
    }
}

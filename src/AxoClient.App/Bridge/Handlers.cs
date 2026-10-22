using System.Diagnostics;
using AxoClient.Core.Common;

namespace AxoClient.App.Bridge;

/// <summary>Was jeder Handler braucht.</summary>
public sealed record HandlerContext(AppHost Host, MainWindow Window, FileRefs Files);

/// <summary>Meldet alle Nachrichtentypen beim <see cref="MessageRouter"/> an (feste Liste).</summary>
public static partial class Handlers
{
    public static void Register(MessageRouter router, HandlerContext ctx)
    {
        RegisterApp(router, ctx);
        RegisterAccounts(router, ctx);
        RegisterInstances(router, ctx);
        RegisterContent(router, ctx);
        RegisterLaunch(router, ctx);
        RegisterServers(router, ctx);
        RegisterSkins(router, ctx);
        RegisterEmotes(router, ctx);
        RegisterSocial(router, ctx);
        RegisterSettings(router, ctx);
        RegisterImport(router, ctx);
    }

    /// <summary>Langer Vorgang als Job: die UI bekommt sofort die Job-ID, Fortschritt kommt per Ereignis.</summary>
    internal static object StartJob(Task task)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.Exception?.InnerException is { } ex and not AxoException and not OperationCanceledException)
                AppLog.Error("Job fehlgeschlagen", ex);
        }, TaskScheduler.Default);
        return new { started = true };
    }

    internal static void OpenInExplorer(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
        else if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + path }, UseShellExecute = false });
        else
            throw new AxoException("errors.notFound");
    }

    /// <summary>Externe Links nur im normalen Browser und nur als HTTPS.</summary>
    internal static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new AxoException("errors.insecureUrl", new() { ["host"] = Text.Clean(url, 60) });
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}

using System.Diagnostics;

namespace AxoClient.UI.Controls;

public static class PrivacyOverlayHost
{
    private static PrivacyLink? _link;

    private static readonly Dictionary<int, PrivacyOverlay> Windows = [];

    private static bool _reported;

    public static void Attach(AppServices app, Process game, string? minecraftVersion)
    {
        try
        {
            _link ??= new PrivacyLink();
            if (Windows.ContainsKey(game.Id))
                return;

            var fontJar = MinecraftFont.FindJar(AppPaths.Versions, minecraftVersion);
            var overlay = new PrivacyOverlay(_link, game, fontJar);
            Windows[game.Id] = overlay;
            overlay.Show();

            if (!_reported && overlay.Problem is { } problem)
            {
                _reported = true;
                _ = app.Dialogs.ShowMessageAsync(
                    overlay.Hidden ? "Privates Overlay eingeschränkt" : "Privates Overlay nicht möglich", problem);
            }

            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => Close(game.Id));
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Privates Overlay öffnen", ex);
        }
    }

    private static void Close(int pid)
    {
        if (!Windows.Remove(pid, out var overlay))
            return;
        try
        {
            overlay.Close();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Privates Overlay schließen", ex);
        }
    }
}

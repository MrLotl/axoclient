namespace AxoClient.UI.Dialogs;

public static class UpdateDialog
{
    private const int MaxNotes = 600;

    public static async Task CheckAsync(AppServices app, bool manual, Action<string> status, Action closeLauncher)
    {
        if (manual && !Updater.IsEnabled)
        {
            await app.Dialogs.ShowMessageAsync("Keine Update-Suche",
                $"Diese Version ({AppInfo.Version}) wurde lokal gebaut, z.B. aus Visual Studio, und sucht nicht " +
                "nach Updates. Updates bekommt nur die AxoClient.exe von GitHub (Releases).");
            return;
        }

        UpdateInfo? update;
        try
        {
            update = await Updater.CheckAsync(app.Http);
        }
        catch (Exception ex)
        {
            if (manual)
                await app.Dialogs.ShowErrorAsync("Update-Suche fehlgeschlagen", ex);
            return;
        }
        if (update == null)
        {
            if (manual)
                await app.Dialogs.ShowMessageAsync("Kein Update", $"Du hast die neueste Version ({AppInfo.Version}).");
            return;
        }

        var notes = update.Notes.Trim();
        if (notes.Length > MaxNotes)
            notes = notes[..MaxNotes] + " ...";
        if (!await app.Dialogs.ConfirmAsync("Update verfügbar",
                $"AxoClient {update.Version} ist verfügbar (du hast {AppInfo.Version})." +
                (notes.Length > 0 ? "\n\n" + notes : "") +
                "\n\nJetzt installieren? AxoClient startet danach neu; laufende Spiele bleiben offen.",
                "Installieren"))
            return;

        try
        {
            await Updater.InstallAsync(app.Http, update, new Progress<double>(p => status($"Update wird geladen... {p:0} %")));
            closeLauncher();
        }
        catch (Exception ex)
        {
            status("");
            await app.Dialogs.ShowErrorAsync("Update fehlgeschlagen", ex,
                "Du kannst die neue Version auch selbst herunterladen:\n" + update.PageUrl);
        }
    }
}

namespace AxoClient.UI.Dialogs;

public static class PreLaunchDialog
{
    public static async Task<bool> ConfirmAsync(AppServices app, Installation inst, IProgress<string> status)
    {
        if (!app.Settings.PreLaunchCheck)
            return true;

        status.Report("Prüfe Instanz...");
        List<Issue> results;
        try
        {
            results = await PreLaunchCheck.RunAsync(app, inst);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Prüfung vor dem Start", ex);
            return true;
        }

        if (results.All(r => r.Severity == IssueSeverity.Info))
            return true;

        var problems = results.Count(r => r.Severity == IssueSeverity.Error);
        var list = new IssueList(results, withMarkers: true);
        var form = Ui.Stack(
            Ui.Scroll(list.View, 360),
            Ui.Note(list.AnyFixable
                ? "Die angehakten Punkte bringt der Launcher vor dem Start in Ordnung. Danach wird gestartet."
                : problems > 0
                    ? "Du kannst trotzdem starten – wenn das Spiel gleich wieder zugeht, liegt es vermutlich hieran."
                    : "Starten geht ohne Weiteres; die Punkte sind nur Hinweise.", 0, 12));

        var subtitle = problems > 0
            ? $"Bei „{inst.Name}“ ist etwas aufgefallen, an dem der Start scheitern kann"
            : $"Bei „{inst.Name}“ sind Kleinigkeiten aufgefallen";
        if (!await app.Dialogs.ShowFormAsync(problems > 0 ? "Der Start könnte scheitern" : "Vor dem Start", form,
                list.AnyFixable ? "Beheben und starten" : "Trotzdem starten", width: 560, subtitle: subtitle,
                icon: problems > 0 ? "Warning" : "Shield"))
            return false;

        var chosen = list.Chosen;
        if (chosen.Count > 0)
            await ApplyFixesAsync(app, chosen, status);
        return true;
    }

    private static async Task ApplyFixesAsync(AppServices app, List<Issue> chosen, IProgress<string> status)
    {
        List<string> done, failed;
        try
        {
            (done, failed) = await app.Dialogs.RunWithProgressAsync("Wird behoben",
                progress => Issue.FixAllAsync(chosen, progress));
        }
        catch (OperationCanceledException)
        {
            status.Report("Beheben abgebrochen; es wird mit dem bisherigen Stand gestartet.");
            return;
        }

        status.Report(string.Join(" ", done));
        if (failed.Count > 0)
            await UiRun.ShowReportAsync(app, "Nicht alles konnte behoben werden", failed);
    }
}

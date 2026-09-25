namespace AxoClient.UI.Dialogs;

public static class CrashDialog
{
    public static async Task ShowAsync(AppServices app, Installation inst, bool justCrashed = false)
    {
        CrashReport? report;
        try
        {
            report = await Task.Run(() => CrashAnalyzer.Analyze(app, inst));
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowErrorAsync("Analyse fehlgeschlagen", ex);
            return;
        }
        if (report == null)
        {
            await app.Dialogs.ShowMessageAsync("Nichts zu analysieren",
                $"Für \"{inst.Name}\" gibt es noch kein Log und keinen Crash-Bericht. Starte die Instanz einmal.");
            return;
        }

        var when = report.When is { } w ? $" ({w:dd.MM.yyyy HH:mm})" : "";
        var findings = new IssueList(report.Findings, withMarkers: false);
        var form = Ui.Stack(
            Ui.Note((justCrashed ? $"\"{inst.Name}\" ist abgestürzt. " : "") +
                    $"Untersucht: {Path.GetFileName(report.SourceFile)}{when}"),
            Ui.Scroll(findings.View, 320),
            Ui.Row(Ui.Button("Bericht öffnen", () => Shell.ShowFile(report.SourceFile!)),
                Ui.Button("Spielordner öffnen", () => Shell.ShowFile(inst.GameDir))));

        if (!await app.Dialogs.ShowFormAsync($"Crash-Analyse: {inst.Name}", form,
                findings.AnyFixable ? "Ausgewählte beheben" : "OK") || !findings.AnyFixable)
            return;

        var selected = findings.Chosen;
        if (selected.Count == 0)
            return;

        var results = await UiRun.RunAsync(app, "Maßnahmen werden ausgeführt", async progress =>
        {
            var (done, failed) = await Issue.FixAllAsync(selected, progress);
            return done.Concat(failed).ToList();
        }, "Maßnahmen fehlgeschlagen");
        if (results == null)
            return;

        app.Instances.NotifyChanged();
        await UiRun.ShowReportAsync(app, "Fertig", results.Append("Starte die Instanz erneut, um es zu testen."));
    }
}

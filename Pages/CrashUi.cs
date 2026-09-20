using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McLauncher.Pages;

/// <summary>Crash-Analyse: zeigt die erkannten Ursachen und führt ausgewählte Maßnahmen direkt aus.</summary>
internal static class CrashUi
{
    public static async Task ShowAsync(AppState app, Installation inst, bool justCrashed = false)
    {
        CrashReport? report = null;
        try
        {
            report = await Task.Run(() => CrashAnalyzer.Analyze(app, inst));
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowMessageAsync("Analyse fehlgeschlagen", ex.Message);
            return;
        }
        if (report == null)
        {
            await app.Dialogs.ShowMessageAsync("Nichts zu analysieren",
                $"Für \"{inst.Name}\" gibt es noch kein Log und keinen Crash-Bericht. Starte die Instanz einmal.");
            return;
        }

        var form = new StackPanel();
        var when = report.When is { } w ? $" ({w:dd.MM.yyyy HH:mm})" : "";
        form.Children.Add(Ui.Note((justCrashed ? $"\"{inst.Name}\" ist abgestürzt. " : "") +
                                  $"Untersucht: {Path.GetFileName(report.SourceFile)}{when}", 10));

        var boxes = new List<(CheckBox Box, CrashFinding Finding)>();
        var list = new StackPanel();
        foreach (var finding in report.Findings)
        {
            list.Children.Add(new TextBlock
            {
                Text = finding.Title,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            list.Children.Add(Ui.Note(finding.Description, 6));
            if (finding.Fix != null)
            {
                var box = Ui.Check(finding.FixText ?? "Beheben");
                box.Margin = new Thickness(0, 0, 0, 14);
                list.Children.Add(box);
                boxes.Add((box, finding));
            }
        }
        form.Children.Add(Ui.Scroll(list, 320));

        var openReport = new Button
        {
            Content = "Bericht öffnen",
            Style = (Style)Application.Current.FindResource("LauncherButton"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        openReport.Click += (_, _) => Open(report.SourceFile!);
        var openFolder = new Button
        {
            Content = "Spielordner öffnen",
            Style = (Style)Application.Current.FindResource("LauncherButton"),
            Padding = new Thickness(12, 6, 12, 6)
        };
        openFolder.Click += (_, _) => Open(inst.GameDir);
        form.Children.Add(Ui.Row(openReport, openFolder));

        var hasFixes = boxes.Count > 0;
        if (!await app.Dialogs.ShowFormAsync($"Crash-Analyse: {inst.Name}", form,
                hasFixes ? "Ausgewählte beheben" : "OK"))
            return;
        if (!hasFixes)
            return;

        var selected = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Finding).ToList();
        if (selected.Count == 0)
            return;

        var results = await UiRun.RunAsync(app, "Maßnahmen werden ausgeführt", async progress =>
        {
            var lines = new List<string>();
            foreach (var finding in selected)
            {
                try
                {
                    progress.Text.Report(finding.FixText ?? finding.Title);
                    lines.Add(await finding.Fix!(progress.Text));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lines.Add($"{finding.Title}: {ex.Message}");
                }
            }
            return lines;
        }, "Maßnahmen fehlgeschlagen");
        if (results == null)
            return;

        app.NotifyInstallationsChanged();
        await UiRun.ShowReportAsync(app, "Fertig", results.Append("Starte die Instanz erneut, um es zu testen."));
    }

    private static void Open(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer nicht startbar
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class CrashDialog
{
    private const int LogLines = 40;

    public static async Task ShowAsync(AppServices app, Installation inst, bool justCrashed = false, Action? playAgain = null)
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
            if (!justCrashed)
                await app.Dialogs.ShowMessageAsync("Nichts zu analysieren",
                    $"Für „{inst.Name}“ gibt es noch kein Log und keinen Crash-Bericht. Starte die Instanz einmal.");
            return;
        }

        var replay = false;
        var title = new TextBlock
        {
            Text = justCrashed ? "Minecraft ist abgestürzt" : "Crash-Analyse",
            Style = Ui.Resource<Style>("DialogTitle"),
            FontSize = 18,
            FontWeight = FontWeights.Bold
        };
        var when = report.When is { } w ? $" · {Formats.Day(w.ToUniversalTime())}, {w:HH:mm}" : "";
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(title);
        titles.Children.Add(DialogParts.Subtitle($"{inst.Name}{when} · {Path.GetFileName(report.SourceFile)}"));
        var header = new DockPanel();
        var tile = DialogParts.IconTile("Warning", danger: true, size: 44);
        tile.CornerRadius = new CornerRadius(12);
        tile.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(tile, Dock.Left);
        var close = DialogParts.CloseButton(app.Dialogs.ClosePanel);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(tile);
        header.Children.Add(close);
        header.Children.Add(titles);

        var body = new StackPanel { Margin = new Thickness(22, 0, 22, 18) };
        var findings = report.Findings
            .OrderBy(f => f.Severity)
            .ThenByDescending(f => f.CanFix)
            .ToList();
        var causes = new StackPanel();
        if (findings.Count == 0)
            causes.Children.Add(Cause("Keine bekannte Ursache gefunden",
                "Im Log steht kein Fehler, den AxoClient kennt. Schau ins Log oder frag im Discord nach.", null));
        for (var i = 0; i < findings.Count; i++)
        {
            var card = Cause(findings[i].Title, findings[i].Description, i == 0 ? "Wahrscheinliche Ursache" : null);
            if (i > 0)
                card.Margin = new Thickness(0, 8, 0, 0);
            causes.Children.Add(card);
        }
        var causeScroll = new ScrollViewer { Content = causes, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(causeScroll);

        var progressText = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("AccentText") };
        var progressPercent = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("AccentText") };
        var progressBar = new ProgressBar { Maximum = 100, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 0) };
        var progressHead = new DockPanel();
        DockPanel.SetDock(progressPercent, Dock.Right);
        progressHead.Children.Add(progressPercent);
        progressHead.Children.Add(progressText);
        var progressCard = new Border
        {
            Style = Ui.Resource<Style>("Inset"),
            Background = Ui.Frozen(Color.FromRgb(0x24, 0x24, 0x24)),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { Children = { progressHead, progressBar } }
        };
        body.Children.Add(progressCard);

        var doneTitle = new TextBlock { Text = "Instanz repariert", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong") };
        var doneText = new TextBlock { FontSize = 12, Foreground = Ui.Resource<Brush>("TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var doneIcon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = Ui.Frozen(Color.FromArgb(0x2E, 0x3F, 0xB9, 0x50)),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Icon { Kind = "Check", Size = 16, Foreground = Ui.Resource<Brush>("Good"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var doneRow = new DockPanel();
        DockPanel.SetDock(doneIcon, Dock.Left);
        doneRow.Children.Add(doneIcon);
        doneRow.Children.Add(new StackPanel { Children = { doneTitle, doneText } });
        var doneCard = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Background = Ui.Resource<Brush>("GoodSoft"),
            BorderBrush = Ui.Frozen(Color.FromArgb(0x40, 0x3F, 0xB9, 0x50)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = doneRow
        };
        body.Children.Add(doneCard);

        var logText = ReadTail(report.SourceFile);
        var log = new TextBox
        {
            Text = logText,
            IsReadOnly = true,
            FontFamily = Ui.Resource<FontFamily>("MonoFont"),
            FontSize = 11.5,
            Foreground = Ui.Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD)),
            Background = Ui.Resource<Brush>("DeepBg"),
            BorderBrush = Ui.Frozen(Color.FromRgb(0x30, 0x30, 0x30)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            MaxHeight = 150,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var chevron = new Icon { Kind = "ChevronRight", Size = 12, Margin = new Thickness(0, 0, 6, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
        var toggleText = new TextBlock { Text = "Log anzeigen", VerticalAlignment = VerticalAlignment.Center };
        var toggle = new Button
        {
            Style = Ui.Resource<Style>("LinkButton"),
            Foreground = Ui.Resource<Brush>("TextSecondary"),
            Margin = new Thickness(0, 12, 0, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { chevron, toggleText } }
        };
        toggle.Click += (_, _) =>
        {
            var show = log.Visibility != Visibility.Visible;
            Ui.Show(log, show);
            toggleText.Text = show ? "Log ausblenden" : "Log anzeigen";
            ((RotateTransform)chevron.RenderTransform).Angle = show ? 90 : 0;
            if (show)
                log.ScrollToEnd();
        };
        body.Children.Add(toggle);
        body.Children.Add(log);

        Button? copy = null;
        copy = DialogParts.Make("TextButton", "Log kopieren", () =>
        {
            try
            {
                Clipboard.SetText(File.ReadAllText(report.SourceFile!));
                copy!.Content = Ui.IconText("Check", "Kopiert", 14);
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Log kopieren", ex);
            }
        }, "Copy");
        var folder = DialogParts.Make("TextButton", "Ordner", () => Shell.ShowFile(report.SourceFile!), "Folder");
        folder.Margin = new Thickness(4, 0, 0, 0);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, folder } };

        var fixable = findings.Where(f => f.CanFix).ToList();
        var closeButton = DialogParts.Secondary("Schließen", app.Dialogs.ClosePanel);
        var action = DialogParts.Primary("Instanz reparieren", () => { }, "Wrench");
        var playButton = DialogParts.Primary("Erneut spielen", () =>
        {
            replay = true;
            app.Dialogs.ClosePanel();
        }, "Play");
        playButton.Visibility = Visibility.Collapsed;
        action.Visibility = fixable.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (fixable.Count == 0 && playAgain != null)
            playButton.Visibility = Visibility.Visible;

        action.Click += async (_, _) =>
        {
            action.IsEnabled = false;
            action.Content = Ui.IconText(null, "Repariere …");
            Ui.Show(causeScroll, false);
            Ui.Show(progressCard, true);
            progressText.Text = "Prüfe " + inst.Name + " …";
            var progress = new WorkProgress(
                new Progress<string>(text => progressText.Text = text),
                new Progress<double>(fraction =>
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = fraction * 100;
                    progressPercent.Text = $"{Math.Round(fraction * 100)} %";
                }),
                CancellationToken.None);
            var (done, failed) = await Issue.FixAllAsync(fixable, progress);
            app.Instances.NotifyChanged();
            Ui.Show(progressCard, false);
            Ui.Show(doneCard, true);
            doneTitle.Text = failed.Count == 0 ? "Instanz repariert" : "Teilweise repariert";
            doneText.Text = string.Join("\n", done.Where(d => d.Length > 0).Concat(failed).DefaultIfEmpty("Alle Maßnahmen wurden ausgeführt."));
            title.Text = failed.Count == 0 ? "Problem behoben" : title.Text;
            Ui.Show(action, false);
            Ui.Show(playButton, playAgain != null);
        };

        var footer = DialogParts.Footer(left, closeButton, action, playButton);
        var top = new Border
        {
            Padding = new Thickness(22, 22, 22, 16),
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            Background = DialogParts.HeroGlow(Color.FromRgb(0xF4, 0x70, 0x67), 0.16),
            Child = header
        };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(body);

        await app.Dialogs.ShowPanelAsync(root, 580);
        if (replay)
            playAgain?.Invoke();
    }

    private static Border Cause(string title, string text, string? label)
    {
        var stack = new StackPanel();
        if (label != null)
        {
            var over = DialogParts.OverLabel(label);
            over.Margin = new Thickness(0, 0, 0, 3);
            stack.Children.Add(over);
        }
        stack.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextStrong"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Ui.Resource<Brush>("TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(9),
            Background = Ui.Resource<Brush>("RowBg"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Icon { Kind = label != null ? "Cube" : "Info", Size = 18, Foreground = Ui.Resource<Brush>("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(stack);
        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = Ui.Frozen(Color.FromRgb(0x24, 0x24, 0x24)),
            BorderBrush = Ui.Frozen(Color.FromRgb(0x34, 0x34, 0x34)),
            BorderThickness = new Thickness(1),
            Child = row
        };
    }

    private static string ReadTail(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path))
                return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Replace("\r", "").Split('\n');
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - LogLines)));
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Log lesen", ex);
            return "";
        }
    }
}

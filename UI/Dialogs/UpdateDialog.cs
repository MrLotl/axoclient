using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class UpdateDialog
{
    private const int MaxNotes = 6;

    public static async Task<UpdateInfo?> CheckQuietlyAsync(AppServices app)
    {
        try
        {
            return await Updater.CheckAsync(app.Http);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Update-Suche", ex);
            return null;
        }
    }

    public static async Task CheckManuallyAsync(AppServices app, Action closeLauncher)
    {
        if (!Updater.IsEnabled)
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
            await app.Dialogs.ShowErrorAsync("Update-Suche fehlgeschlagen", ex);
            return;
        }
        if (update == null)
            await app.Dialogs.ShowMessageAsync("Kein Update", $"Du hast die neueste Version ({AppInfo.ShortVersion}).");
        else
            await ShowAsync(app, update, closeLauncher);
    }

    public static Task InstallAsync(AppServices app, UpdateInfo update, Action closeLauncher)
    {
        new UpdaterWindow(app.Http, update).Show();
        closeLauncher();
        return Task.CompletedTask;
    }

    public static async Task ShowAsync(AppServices app, UpdateInfo update, Action closeLauncher)
    {
        var install = false;
        var versions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        versions.Children.Add(DialogParts.Chip("v" + AppInfo.ShortVersion));
        versions.Children.Add(new Icon
        {
            Kind = "ArrowRight",
            Size = 14,
            Foreground = Ui.Resource<Brush>("MutedText"),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        versions.Children.Add(DialogParts.Chip("v" + update.Version, accent: true));
        if (update.Size > 0)
            versions.Children.Add(new TextBlock
            {
                Text = $"· {(update.Size / 1048576.0).ToString("0", CultureInfo.InvariantCulture)} MB",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = Ui.Resource<Brush>("MutedText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "Update verfügbar", Style = Ui.Resource<Style>("DialogTitle"), FontSize = 18, FontWeight = FontWeights.Bold });
        titleStack.Children.Add(versions);

        var header = new DockPanel();
        var logo = new Image { Source = Ui.Resource<ImageSource>("LogoGills"), Width = 52, Height = 52, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(logo, Dock.Left);
        var close = DialogParts.CloseButton(app.Dialogs.ClosePanel);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(logo);
        header.Children.Add(close);
        header.Children.Add(titleStack);

        var body = new StackPanel { Margin = new Thickness(22, 4, 22, 18) };
        var notes = Notes(update.Notes);
        if (notes.Count > 0)
        {
            body.Children.Add(DialogParts.OverLabel("Neu in dieser Version"));
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var note in notes)
                list.Children.Add(DialogParts.Bullet(note));
            body.Children.Add(list);
        }
        var link = new Button { Style = Ui.Resource<Style>("LinkButton"), Content = "Alle Änderungen ansehen" };
        link.Click += (_, _) => Shell.OpenUrl(update.PageUrl);
        body.Children.Add(link);

        var auto = new CheckBox
        {
            Content = "Künftig automatisch",
            IsChecked = app.Settings.AutoUpdate,
            FontSize = 12.5,
            Foreground = Ui.Resource<Brush>("TextSecondary")
        };
        auto.Click += (_, _) =>
        {
            app.Settings.AutoUpdate = auto.IsChecked == true;
            app.SaveSettings();
        };

        var footer = DialogParts.Footer(auto,
            DialogParts.Secondary("Später", app.Dialogs.ClosePanel),
            DialogParts.Primary("Jetzt aktualisieren", () =>
            {
                install = true;
                app.Dialogs.ClosePanel();
            }, "Download"));

        var root = new DockPanel();
        var top = new Border
        {
            Padding = new Thickness(22, 22, 22, 18),
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            Background = DialogParts.HeroGlow(Color.FromRgb(0xEC, 0x48, 0x99), 0.22, Color.FromRgb(0x8B, 0x5C, 0xF6), 0.12),
            Child = header
        };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(body);

        await app.Dialogs.ShowPanelAsync(root, 480);
        if (install)
        {
            if (!Updater.IsEnabled)
            {
                await app.Dialogs.ShowMessageAsync("Keine Update-Installation",
                    "Diese Version wurde lokal gebaut und kann sich nicht selbst aktualisieren.");
                return;
            }
            await InstallAsync(app, update, closeLauncher);
        }
    }

    private static List<string> Notes(string markdown) =>
        markdown.Replace("\r", "").Split('\n')
            .Select(line => line.Trim().TrimStart('-', '*', '•').Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Replace("**", "").Replace("`", ""))
            .Take(MaxNotes)
            .ToList();
}

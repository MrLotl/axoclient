using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Dialogs;

public static partial class AdminsDialog
{
    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex PlayerName();

    public static async Task ShowAsync(AppServices app)
    {
        var list = new StackPanel();
        var status = Ui.Note("Lädt...", 0);
        var input = Ui.Input();
        input.MaxLength = 16;
        input.Width = 250;
        input.Margin = new Thickness(0, 0, 8, 0);
        var busy = false;

        async Task RunAsync(Func<Task<string>> action)
        {
            if (busy)
                return;
            busy = true;
            try
            {
                status.Foreground = Ui.Resource<Brush>("SubtleText");
                status.Text = await action();
            }
            catch (Exception ex)
            {
                status.Foreground = Ui.ProblemBrush;
                status.Text = ErrorReport.Short(ex);
            }
            finally
            {
                busy = false;
            }
        }

        async Task ReloadAsync()
        {
            var admins = await app.Axo.GetAdminsAsync();
            list.Children.Clear();
            if (admins.Count == 0)
                list.Children.Add(Ui.Note("Noch keine Admins ernannt.", 0));
            foreach (var admin in admins)
                list.Children.Add(Row(admin));
        }

        UIElement Row(AdminInfo admin)
        {
            var remove = new Button
            {
                Style = Ui.Resource<Style>("IconButton"),
                Content = "",
                Width = 30,
                Height = 30,
                FontSize = 12,
                ToolTip = "Admin-Rechte entziehen"
            };
            remove.Click += async (_, _) => await RunAsync(async () =>
            {
                await app.Axo.RemoveAdminAsync(admin.Uuid);
                await ReloadAsync();
                return $"{admin.Name} ist kein Admin mehr.";
            });
            DockPanel.SetDock(remove, Dock.Right);

            var head = new Image
            {
                Source = new BitmapImage(new Uri(admin.HeadUrl)),
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(head, Dock.Left);

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(remove);
            row.Children.Add(head);
            row.Children.Add(new TextBlock { Text = admin.Name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        void Add()
        {
            var name = input.Text.Trim();
            if (!PlayerName().IsMatch(name))
            {
                status.Foreground = Ui.ProblemBrush;
                status.Text = "Ein Spielername besteht aus 1 bis 16 Buchstaben, Ziffern oder _.";
                return;
            }
            _ = RunAsync(async () =>
            {
                var added = await app.Axo.AddAdminAsync(name);
                input.Text = "";
                await ReloadAsync();
                return $"{added.Name} ist jetzt Admin.";
            });
        }

        bool AddOrClose()
        {
            if (input.Text.Trim().Length == 0)
                return true;
            Add();
            return false;
        }

        var form = Ui.Stack(
            Ui.Note("Admins dürfen AxoClient-Umhänge hochladen und hochgeladene Umhänge löschen. " +
                    "Du bist als Besitzer immer Admin."),
            Ui.Label("Spielername"),
            Ui.Row(input, Ui.Button("Hinzufügen", Add)),
            Ui.Label("Admins"),
            Ui.Scroll(list, 260),
            status);

        _ = RunAsync(async () =>
        {
            await ReloadAsync();
            return "";
        });
        await app.Dialogs.ShowFormAsync("Admins verwalten", form, "Fertig", AddOrClose);
    }
}

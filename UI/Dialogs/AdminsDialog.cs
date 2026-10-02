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
                Style = Ui.Resource<Style>("ActTrash"),
                Content = new Icon { Kind = "Trash", Size = 15 },
                Width = 32,
                Height = 32,
                Margin = new Thickness(0),
                ToolTip = "Admin-Rechte entziehen"
            };
            remove.Click += async (_, _) => await RunAsync(async () =>
            {
                await app.Axo.RemoveAdminAsync(admin.Uuid);
                await ReloadAsync();
                return $"{admin.Name} ist kein Admin mehr.";
            });
            DockPanel.SetDock(remove, Dock.Right);

            var head = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 12, 0),
                Background = Ui.Resource<Brush>("ChipBg"),
                Child = Ui.UrlImage(admin.HeadUrl)
            };
            DockPanel.SetDock(head, Dock.Left);

            var row = new DockPanel();
            row.Children.Add(remove);
            row.Children.Add(head);
            row.Children.Add(new TextBlock
            {
                Text = admin.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Resource<Brush>("TextStrong"),
                VerticalAlignment = VerticalAlignment.Center
            });
            return new Border
            {
                Background = Ui.Resource<Brush>("RowBg"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 8, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = row
            };
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
            AddRow(input, Ui.Button("Hinzufügen", Add)),
            Ui.Label("Admins"),
            Ui.Scroll(list, 260),
            status);

        _ = RunAsync(async () =>
        {
            await ReloadAsync();
            return "";
        });
        await app.Dialogs.ShowFormAsync("Admins verwalten", form, "Fertig", AddOrClose, 460,
            "Wer AxoClient-Umhänge hochladen und löschen darf", "Shield");
    }

    private static DockPanel AddRow(TextBox input, Button add)
    {
        add.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(add, Dock.Right);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(add);
        row.Children.Add(input);
        return row;
    }
}

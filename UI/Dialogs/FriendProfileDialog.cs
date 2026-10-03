using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class FriendProfileDialog
{
    public static async Task ShowAsync(AppServices app, FriendInfo friend, FriendsPanel panel)
    {
        var viewer = new SkinViewer { Width = 200, Height = 330, VerticalAlignment = VerticalAlignment.Bottom };
        var left = new Border
        {
            Width = 230,
            CornerRadius = new CornerRadius(16, 0, 0, 16),
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.85),
                GradientOrigin = new Point(0.5, 0.85),
                RadiusX = 1.2,
                RadiusY = 0.7,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x38, 0x38, 0x38), 0),
                    new GradientStop(Color.FromRgb(0x2C, 0x2C, 0x2C), 0.55),
                    new GradientStop(Color.FromRgb(0x25, 0x25, 0x25), 1)
                }
            }
        };
        var leftLayers = new Grid();
        leftLayers.SizeChanged += (_, e) =>
        {
            var height = Math.Max(200, Math.Min(e.NewSize.Height - 70, (e.NewSize.Width - 30) * 1.65));
            viewer.Height = height;
            viewer.Width = height / 1.65;
        };
        leftLayers.Children.Add(new Border
        {
            Height = 120,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(16, 0, 0, 0),
            Background = new LinearGradientBrush(Color.FromArgb(0x2E, 0xEC, 0x48, 0x99), Color.FromArgb(0, 0x8B, 0x5C, 0xF6), 90)
        });
        viewer.Margin = new Thickness(0, 0, 0, 18);
        leftLayers.Children.Add(viewer);
        left.Child = leftLayers;

        var head = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true,
            Background = Ui.Resource<Brush>("RowBg"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = Ui.UrlImage(friend.HeadUrl)
        };
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = friend.Name,
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.Resource<Brush>("TextStrong")
        });
        var activity = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        activity.Children.Add(Ui.Dot(friend.Presence));
        activity.Children.Add(new TextBlock
        {
            Text = friend.ActivityText,
            FontSize = 12.5,
            Foreground = Ui.Resource<Brush>("TextSecondary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 330
        });
        titleStack.Children.Add(activity);
        var header = DialogParts.Header("", null, null, app.Dialogs.ClosePanel);
        header.Children.RemoveAt(header.Children.Count - 1);
        DockPanel.SetDock(head, Dock.Left);
        header.Children.Insert(0, head);
        header.Children.Add(titleStack);

        var stats = new UniformGrid3();
        var sinceValue = Stat(stats, "Freunde seit", friend.SinceUtc is { } since ? MonthYear(since) : "–");
        var togetherValue = Stat(stats, "Zusammen gespielt", "–");
        var commonValue = Stat(stats, "Gemeinsame Server", "–");

        var serversSection = Section("Gemeinsame Server");
        var serverChips = new WrapPanel();
        serversSection.Children.Add(serverChips);
        var recentSection = Section("Zuletzt");
        var recentList = new StackPanel();
        recentSection.Children.Add(recentList);
        recentList.Children.Add(new TextBlock { Text = "Wird geladen …", FontSize = 12.5, Foreground = Ui.Resource<Brush>("DimText") });

        var actions = new DockPanel { LastChildFill = false };
        var remove = DialogParts.Make("DangerTextButton", "Freund entfernen", () =>
        {
            app.Dialogs.ClosePanel();
            _ = panel.RemoveAsync(friend);
        });
        DockPanel.SetDock(remove, Dock.Right);
        actions.Children.Add(remove);
        if (friend.CanJoin)
        {
            var join = DialogParts.Make("GoodButton", "Nachjoinen", () =>
            {
                app.Dialogs.ClosePanel();
                _ = panel.JoinAsync(friend);
            }, "Play");
            join.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(join);
        }
        var invite = DialogParts.Make("SecondButton", "Einladen", () => _ = InviteAsync(app, friend), "Send");
        invite.IsEnabled = friend.IsFriend;
        actions.Children.Add(invite);

        var right = new DockPanel { Margin = new Thickness(22) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(actions, Dock.Bottom);
        right.Children.Add(header);
        right.Children.Add(actions);
        var body = new StackPanel { Margin = new Thickness(0, 16, 0, 16) };
        body.Children.Add(stats);
        serversSection.Margin = new Thickness(0, 16, 0, 0);
        recentSection.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(serversSection);
        body.Children.Add(recentSection);
        right.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);

        _ = LoadSkinAsync(app, friend, viewer);
        _ = LoadProfileAsync(app, friend, togetherValue, commonValue, sinceValue, serverChips, recentList, serversSection);
        await app.Dialogs.ShowPanelAsync(root, host =>
        {
            var width = Math.Clamp(host.Width * 0.62, 680, 1100);
            root.Height = Math.Max(320, Math.Min(Math.Clamp(host.Height * 0.62, 420, 820), host.Height - 80));
            left.Width = Math.Clamp(width * 0.36, 230, 400);
            return width;
        });
    }

    private static async Task LoadSkinAsync(AppServices app, FriendInfo friend, SkinViewer viewer)
    {
        if (await PlayerSkins.LoadAsync(app.Http, friend.Uuid) is { } skin)
            viewer.SetSkin(skin.Png, skin.Slim, skin.CapePng);
    }

    private static async Task LoadProfileAsync(AppServices app, FriendInfo friend, TextBlock together, TextBlock common,
        TextBlock since, WrapPanel chips, StackPanel recent, StackPanel serversSection)
    {
        FriendProfile? profile = null;
        try
        {
            if (friend.IsFriend)
                profile = await app.Axo.GetFriendProfileAsync(friend.Uuid);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Freundesprofil laden", ex);
        }

        recent.Children.Clear();
        if (profile == null)
        {
            recent.Children.Add(new TextBlock
            {
                Text = "Noch keine Aktivität.",
                FontSize = 12.5,
                Foreground = Ui.Resource<Brush>("DimText")
            });
            Ui.Show(serversSection, false);
            return;
        }

        if (profile.SinceUtc is { } s)
            since.Text = MonthYear(s);
        together.Text = profile.TogetherSeconds > 0 ? Formats.Duration(profile.TogetherSeconds) : "–";
        common.Text = profile.Servers.Count.ToString(CultureInfo.InvariantCulture);
        foreach (var server in profile.Servers)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Icon { Kind = "Server", Size = 14, Foreground = Ui.Resource<Brush>("TextSecondary"), Margin = new Thickness(0, 0, 7, 0) });
            row.Children.Add(new TextBlock { Text = server, FontSize = 12.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
            chips.Children.Add(new Border
            {
                Height = 30,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(8),
                Background = Ui.Resource<Brush>("RowBg"),
                Margin = new Thickness(0, 0, 6, 6),
                Child = row
            });
        }
        Ui.Show(serversSection, profile.Servers.Count > 0);

        if (profile.Recent.Count == 0)
            recent.Children.Add(new TextBlock { Text = "Noch keine Aktivität.", FontSize = 12.5, Foreground = Ui.Resource<Brush>("DimText") });
        foreach (var (when, text) in profile.Recent.Take(10))
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var whenText = new TextBlock { Text = Formats.Day(when), Width = 64, FontSize = 12.5, Foreground = Ui.Resource<Brush>("DimText") };
            DockPanel.SetDock(whenText, Dock.Left);
            line.Children.Add(whenText);
            line.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Ui.Frozen(Color.FromRgb(0xD0, 0xD0, 0xD0)), TextTrimming = TextTrimming.CharacterEllipsis });
            recent.Children.Add(line);
        }
    }

    private static async Task InviteAsync(AppServices app, FriendInfo friend)
    {
        var current = app.Games.CurrentServer;
        var inst = app.Instances.Selected;
        string? server = current?.Server;
        string? version = current?.Version ?? inst?.MinecraftVersion;
        if (server == null)
        {
            var hosted = app.LocalServers.Servers
                .Where(s => app.LocalServers.StateOf(s)?.FriendAddress != null)
                .Select(s => (Name: s.Name + " (dein Server)", Address: app.LocalServers.StateOf(s)!.FriendAddress!,
                    Version: s.IsProxy || s.Version.Length == 0 ? null : s.Version))
                .ToList();
            var options = hosted.Select(h => (h.Name, h.Address))
                .Concat(inst != null ? new ServerStore(inst.GameDir).Addresses() : []).ToList();
            if (options.Count == 0)
            {
                await app.Dialogs.ShowMessageAsync("Kein Server",
                    "Es gibt noch keinen Server, auf den du einladen kannst. Füge einer Instanz einen Server hinzu oder starte einen lokalen Server.");
                return;
            }
            var combo = Ui.Combo(options.Select(o => (object)(o.Name == o.Address ? o.Address : $"{o.Name} · {o.Address}")));
            combo.SelectedIndex = 0;
            if (!await app.Dialogs.ShowFormAsync($"{friend.Name} einladen", Ui.Stack(Ui.Label("Server"), combo), "Einladung senden",
                    null, 420, "Dein Freund bekommt eine Benachrichtigung und kann direkt beitreten.", "Send"))
                return;
            var picked = Math.Max(0, combo.SelectedIndex);
            server = options[picked].Address;
            if (picked < hosted.Count)
                version = hosted[picked].Version ?? version;
        }
        if (app.LocalServers.FriendAddressFor(server) is not { } shared)
        {
            await app.Dialogs.ShowMessageAsync("Server nicht erreichbar",
                $"„{server}“ ist nur auf deinem PC erreichbar. Starte den passenden lokalen Server, damit Freunde beitreten können.");
            return;
        }
        server = shared;
        await UiRun.GuardAsync(app, "Einladung fehlgeschlagen", async () =>
        {
            await app.Axo.InviteAsync(friend.Uuid, server, version);
            Toasts.Show(new Toast($"Einladung an {friend.Name} gesendet", server, BadgeIcon: "Send"));
        });
    }

    private static TextBlock Stat(UniformGrid3 grid, string label, string value)
    {
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Ui.Resource<Brush>("MutedText") });
        stack.Children.Add(valueText);
        grid.Add(new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Resource<Brush>("RowBg"),
            Child = stack
        });
        return valueText;
    }

    private static StackPanel Section(string title)
    {
        var stack = new StackPanel();
        var label = DialogParts.OverLabel(title);
        label.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(label);
        return stack;
    }

    private static string MonthYear(DateTime utc) =>
        utc.ToLocalTime().ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));

    private sealed class UniformGrid3 : Grid
    {
        public UniformGrid3()
        {
            for (var i = 0; i < 3; i++)
                ColumnDefinitions.Add(new ColumnDefinition());
        }

        public void Add(UIElement element)
        {
            var index = Children.Count;
            SetColumn(element, index);
            if (element is FrameworkElement fe && index > 0)
                fe.Margin = new Thickness(8, 0, 0, 0);
            Children.Add(element);
        }
    }
}

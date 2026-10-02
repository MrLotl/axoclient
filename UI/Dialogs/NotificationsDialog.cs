using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class NotificationsDialog
{
    public static async Task ShowAsync(AppServices app, NotificationCenter center, HomePage home)
    {
        var list = new StackPanel();
        var unread = center.UnreadCount;

        var header = new DockPanel { Margin = new Thickness(20, 20, 20, 14) };
        var close = DialogParts.CloseButton(app.Dialogs.ClosePanel);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Benachrichtigungen", Style = Ui.Resource<Style>("DialogTitle") });
        if (unread > 0)
            title.Children.Add(new Border
            {
                Height = 20,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(10, 0, 0, 0),
                CornerRadius = new CornerRadius(10),
                Background = Ui.Resource<Brush>("Accent"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = unread + " neu",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        header.Children.Add(title);

        void Render()
        {
            list.Children.Clear();
            var items = center.Items;
            if (items.Count == 0)
            {
                list.Children.Add(new StackPanel
                {
                    Margin = new Thickness(0, 30, 0, 40),
                    Children =
                    {
                        new Icon { Kind = "Bell", Size = 28, Foreground = Ui.Resource<Brush>("DimText"), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = "Keine Benachrichtigungen", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4) },
                        new TextBlock { Text = "Hier landen Einladungen, geteilte Instanzen und Updates.", FontSize = 12.5, Foreground = Ui.Resource<Brush>("DimText"), HorizontalAlignment = HorizontalAlignment.Center }
                    }
                });
                return;
            }
            foreach (var notice in items)
                list.Children.Add(Row(app, center, home, notice, Render));
        }

        Render();
        var scroller = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 540,
            Padding = new Thickness(14, 0, 8, 14),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(scroller);

        await app.Dialogs.ShowPanelAsync(root, 680);
        await center.MarkAllReadAsync();
    }

    private static Border Row(AppServices app, NotificationCenter center, HomePage home, Notice notice, Action rerender)
    {
        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = Ui.Resource<Brush>("RowBg"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0),
            Child = notice.HeadUrl != null ? Ui.UrlImage(notice.HeadUrl)
                : notice.Image != null ? new Image { Source = notice.Image, Stretch = Stretch.UniformToFill }
                : new Image { Source = Ui.Resource<ImageSource>("LogoGills"), Margin = new Thickness(5) }
        };

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = notice.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (notice.Text.Length > 0 && notice.Kind != NoticeKind.Share)
            body.Children.Add(new TextBlock
            {
                Text = notice.Text,
                FontSize = 12.5,
                Foreground = Ui.Resource<Brush>("TextSecondary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

        var actions = Actions(app, center, home, notice, rerender);
        if (notice.Share != null)
            body.Children.Add(ShareCard(app, center, notice, rerender));
        else if (actions != null)
            body.Children.Add(actions);
        body.Children.Add(new TextBlock
        {
            Text = Formats.Ago(notice.TimeUtc),
            FontSize = 11.5,
            Foreground = Ui.Resource<Brush>("DimText"),
            Margin = new Thickness(0, 4, 0, 0)
        });

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (notice.Unread)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Ui.Resource<Brush>("AccentBright"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 5, 0, 0),
                ToolTip = "Ungelesen"
            };
            DockPanel.SetDock(dot, Dock.Right);
            row.Children.Add(dot);
        }
        row.Children.Add(body);

        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 4),
            Background = notice.Unread ? UnreadBrush : Brushes.Transparent,
            Child = row
        };
    }

    private static readonly Brush UnreadBrush = MakeUnreadBrush();

    private static Brush MakeUnreadBrush()
    {
        var brush = new LinearGradientBrush(Color.FromRgb(0x3A, 0x2E, 0x38), Color.FromRgb(0x33, 0x2F, 0x3C), 45);
        brush.Freeze();
        return brush;
    }

    private static StackPanel? Actions(AppServices app, NotificationCenter center, HomePage home, Notice notice, Action rerender)
    {
        Button? primary = null;
        switch (notice.Kind)
        {
            case NoticeKind.Invite when notice.Remote?.Server is { } server:
                primary = Small("SmallGoodButton", "Beitreten", "Join", () =>
                {
                    app.Dialogs.ClosePanel();
                    _ = home.JoinServerAsync(server, notice.Remote.Version, notice.Remote.FromName, ask: true);
                });
                break;
            case NoticeKind.FriendRequest when notice.Remote?.FromName is { } name:
                primary = Small("SmallPrimaryButton", "Annehmen", "Check", () => _ = UiRun.GuardAsync(app,
                    "Anfrage konnte nicht angenommen werden", async () =>
                    {
                        await app.Axo.AddFriendAsync(name);
                        center.Forget(notice);
                        rerender();
                        home.RefreshFriends();
                    }));
                break;
            case NoticeKind.AppUpdate when notice.Update is { } update:
                primary = Small("SmallPrimaryButton", "Jetzt aktualisieren", "Download", () =>
                {
                    app.Dialogs.ClosePanel();
                    _ = UpdateDialog.ShowAsync(app, update, () => Application.Current.MainWindow?.Close());
                });
                break;
            case NoticeKind.ModUpdates when notice.Instance is { } inst:
                primary = Small("SmallButton", "Ansehen", "ArrowRight", () =>
                {
                    app.Dialogs.ClosePanel();
                    home.OpenInstance(inst, InstanceSection.Mods);
                });
                break;
        }
        if (primary == null)
            return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(primary);
        return row;
    }

    private static Button Small(string style, string text, string icon, Action click)
    {
        var button = DialogParts.Make(style, text, click, icon);
        button.Height = 32;
        button.FontSize = 12.5;
        return button;
    }

    private static Border ShareCard(AppServices app, NotificationCenter center, Notice notice, Action rerender)
    {
        var share = notice.Share!;
        var isInstance = share.Kind == ShareKinds.Instance;
        var thumb = new Border
        {
            Width = 48,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = Ui.Resource<Brush>("AccentSoft"),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new Icon
            {
                Kind = share.Kind switch { ShareKinds.Instance => "Library", ShareKinds.Server => "Server", _ => "Cube" },
                Size = 16,
                Foreground = Ui.Resource<Brush>("AccentText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var meta = new TextBlock { Text = "", FontSize = 11.5, Foreground = Ui.Resource<Brush>("MutedText") };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = share.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(meta);
        var tag = new Border
        {
            Style = Ui.Resource<Style>("Tag"),
            Child = new TextBlock
            {
                Text = ShareKinds.Label(share.Kind),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var top = new DockPanel();
        DockPanel.SetDock(thumb, Dock.Left);
        DockPanel.SetDock(tag, Dock.Right);
        top.Children.Add(thumb);
        top.Children.Add(tag);
        top.Children.Add(texts);

        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };

        var download = Small("SmallPrimaryButton", isInstance ? "Als neue Instanz herunterladen" : "Herunterladen", "Download",
            () =>
            {
                app.Dialogs.ClosePanel();
                _ = OpenShareAsync(app, center, notice);
            });
        var decline = Small("TextButton", "Ablehnen", "Close", () => _ = UiRun.GuardAsync(app, "Ablehnen fehlgeschlagen",
            async () =>
            {
                await app.Axo.DeleteShareAsync(share.Id);
                center.Forget(notice);
                rerender();
            }));
        decline.Margin = new Thickness(8, 0, 0, 0);
        var source = new TextBlock
        {
            Text = "Offizielle Dateien von Modrinth",
            FontSize = 11,
            Foreground = Ui.Resource<Brush>("DimText"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var actionRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(download, Dock.Left);
        DockPanel.SetDock(decline, Dock.Left);
        actionRow.Children.Add(download);
        actionRow.Children.Add(decline);
        source.HorizontalAlignment = HorizontalAlignment.Right;
        actionRow.Children.Add(source);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(chips);
        stack.Children.Add(actionRow);
        _ = FillDetailsAsync(app, share, meta, chips);

        return new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Resource<Brush>("InsetBg"),
            BorderBrush = Ui.Resource<Brush>("PopupLine"),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    private static async Task FillDetailsAsync(AppServices app, ShareInfo share, TextBlock meta, WrapPanel chips)
    {
        try
        {
            var json = await app.Axo.GetSharePayloadAsync(share.Id);
            var labels = new List<string>();
            switch (share.Kind)
            {
                case ShareKinds.Instance:
                    var manifest = ShareJson.Read<InstanceManifest>(json, share.Kind);
                    meta.Text = manifest.Loader == LoaderType.Vanilla
                        ? $"{manifest.Minecraft} · Vanilla"
                        : $"{manifest.Minecraft} · {manifest.Loader}";
                    foreach (var group in manifest.Content.GroupBy(c => c.Type))
                        labels.Add(Formats.Count(group.Count(), ContentTypes.Label(group.Key), ContentTypes.PluralLabel(group.Key)));
                    if (manifest.Servers.Count > 0)
                        labels.Add(Formats.Count(manifest.Servers.Count, "Server", "Server"));
                    if (manifest.Options != null)
                        labels.Add("Einstellungen");
                    break;
                case ShareKinds.Content:
                    var content = ShareJson.Read<ContentPayload>(json, share.Kind);
                    meta.Text = content.KindText;
                    break;
                case ShareKinds.Server:
                    var server = ShareJson.Read<ServerPayload>(json, share.Kind);
                    meta.Text = server.Address;
                    break;
            }
            foreach (var label in labels)
                chips.Children.Add(new Border
                {
                    Height = 22,
                    Padding = new Thickness(8, 0, 8, 0),
                    CornerRadius = new CornerRadius(11),
                    Background = Ui.Frozen(Color.FromRgb(0x30, 0x30, 0x30)),
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Ui.Resource<Brush>("TextSecondary"),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            Ui.Show(chips, labels.Count > 0);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Geteiltes Paket ansehen", ex);
            Ui.Show(chips, false);
        }
    }

    private static async Task OpenShareAsync(AppServices app, NotificationCenter center, Notice notice)
    {
        if (await ShareDialogs.OpenShareAsync(app, notice.Share!))
            center.Forget(notice);
        await center.RefreshAsync();
    }
}

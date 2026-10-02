using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class DialogParts
{
    public static Button CloseButton(Action close)
    {
        var button = new Button
        {
            Style = Ui.Resource<Style>("GhostIconButton"),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Schließen",
            Content = new Icon { Kind = "Close", Size = 14 }
        };
        button.Click += (_, _) => close();
        return button;
    }

    public static Border IconTile(string icon, bool danger = false, double size = 40)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 4),
            Background = Ui.Resource<Brush>(danger ? "DangerSoft" : "AccentSoft"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Icon
            {
                Kind = icon,
                Size = size * 0.45,
                Foreground = danger ? Ui.Frozen(Color.FromRgb(0xFF, 0x8A, 0x80)) : Ui.Resource<Brush>("AccentText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    public static DockPanel Header(string title, string? subtitle, FrameworkElement? leading, Action close,
        double titleSize = 17)
    {
        var dock = new DockPanel();
        if (leading != null)
        {
            leading.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(leading, Dock.Left);
            dock.Children.Add(leading);
        }
        var closeButton = CloseButton(close);
        closeButton.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(closeButton, Dock.Right);
        dock.Children.Add(closeButton);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            Style = Ui.Resource<Style>("DialogTitle"),
            FontSize = titleSize
        });
        if (!string.IsNullOrEmpty(subtitle))
            texts.Children.Add(Subtitle(subtitle));
        dock.Children.Add(texts);
        return dock;
    }

    public static TextBlock Subtitle(string text) => new()
    {
        Text = text,
        Style = Ui.Resource<Style>("Body"),
        Foreground = Ui.Resource<Brush>("MutedText"),
        Margin = new Thickness(0, 4, 0, 0)
    };

    public static Border Footer(UIElement? left, params UIElement[] right)
    {
        var dock = new DockPanel { LastChildFill = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var element in right)
        {
            if (element is FrameworkElement fe && buttons.Children.Count > 0)
                fe.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(element);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        if (left != null)
        {
            DockPanel.SetDock(left, Dock.Left);
            dock.Children.Add(left);
        }
        return new Border
        {
            BorderBrush = Ui.Resource<Brush>("Divider"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 14, 22, 14),
            Child = dock
        };
    }

    public static Button Primary(string text, Action click, string? icon = null) =>
        Make("PrimaryButton", text, click, icon);

    public static Button Secondary(string text, Action click, string? icon = null) =>
        Make("LauncherButton", text, click, icon);

    public static Button Make(string style, string text, Action click, string? icon = null)
    {
        var button = new Button { Style = Ui.Resource<Style>(style), Content = Ui.IconText(icon, text) };
        button.Click += (_, _) => click();
        return button;
    }

    public static TextBlock OverLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = Ui.Resource<Style>("OverLabel")
    };

    public static Border Chip(string text, bool accent = false) => new()
    {
        Height = 22,
        Padding = new Thickness(8, 0, 8, 0),
        CornerRadius = new CornerRadius(6),
        Background = accent ? Ui.Resource<Brush>("Accent") : Ui.Resource<Brush>("ChipBg"),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent ? Brushes.White : Ui.Resource<Brush>("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    public static StackPanel Bullet(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = Ui.Resource<Brush>("AccentBright"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 10, 0)
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = Ui.Resource<Brush>("SubtleText"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 390,
            LineHeight = 19
        });
        return row;
    }

    public static Brush HeroGlow(Color first, double firstAlpha, Color? second = null, double secondAlpha = 0)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0, 0),
            GradientOrigin = new Point(0, 0),
            RadiusX = 1.2,
            RadiusY = 1.4
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(firstAlpha * 255), first.R, first.G, first.B), 0));
        if (second is { } s)
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(secondAlpha * 255), s.R, s.G, s.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x2A, 0x2A, 0x2A), 0.75));
        brush.Freeze();
        return brush;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AxoClient.UI;

public record Toast(string Title, string Text, ImageSource? Image = null, string BadgeIcon = "Send",
    string? ActionText = null, Action? OnAction = null, double Seconds = 7);

public sealed class ToastHost : StackPanel
{
    public ToastHost()
    {
        Width = 360;
        VerticalAlignment = VerticalAlignment.Bottom;
    }

    public void Show(Toast toast)
    {
        var card = Build(toast, out var bar);
        Children.Add(card);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(toast.Seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Remove(card);
        };
        timer.Start();

        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 16);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        card.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase() });
        bar.RenderTransform = new ScaleTransform(1, 1);
        bar.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromSeconds(toast.Seconds)));
        card.Tag = timer;
    }

    private void Remove(FrameworkElement card)
    {
        (card.Tag as DispatcherTimer)?.Stop();
        Children.Remove(card);
    }

    private Border Build(Toast toast, out Border bar)
    {
        var image = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = Ui.Resource<Brush>("RowBg"),
            ClipToBounds = true,
            Child = toast.Image != null
                ? new Image { Source = toast.Image, Stretch = Stretch.UniformToFill }
                : new Image { Source = Ui.Resource<ImageSource>("LogoGills"), Margin = new Thickness(6) }
        };
        RenderOptions.SetBitmapScalingMode(image.Child, BitmapScalingMode.NearestNeighbor);
        var badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Ui.Resource<Brush>("Accent"),
            BorderBrush = Ui.Resource<Brush>("RowBgSoft"),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -5, -5),
            Child = new Icon { Kind = toast.BadgeIcon, Size = 10, Foreground = Brushes.White, StrokeWidth = 2.2 }
        };
        var visual = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        visual.Children.Add(image);
        visual.Children.Add(badge);

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock
        {
            Text = toast.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            TextWrapping = TextWrapping.Wrap
        });
        texts.Children.Add(new TextBlock
        {
            Text = toast.Text,
            FontSize = 12.5,
            Foreground = Ui.Resource<Brush>("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });

        Border? card = null;
        if (toast.ActionText != null)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var action = new Button
            {
                Content = toast.ActionText,
                Style = Ui.Resource<Style>("SmallPrimaryButton"),
                Height = 30,
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0)
            };
            action.Click += (_, _) =>
            {
                Remove(card!);
                toast.OnAction?.Invoke();
            };
            var later = new Button { Content = "Später", Style = Ui.Resource<Style>("SmallButton"), Height = 30, FontSize = 12 };
            later.Click += (_, _) => Remove(card!);
            buttons.Children.Add(action);
            buttons.Children.Add(later);
            texts.Children.Add(buttons);
        }

        var close = new Button
        {
            Style = Ui.Resource<Style>("GhostIconButton"),
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Hinweis schließen",
            Content = new Icon { Kind = "Close", Size = 12 }
        };
        close.Click += (_, _) => Remove(card!);

        var row = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(visual, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(visual);
        row.Children.Add(close);
        row.Children.Add(texts);

        bar = new Border
        {
            Height = 3,
            Background = Ui.Resource<Brush>("AccentHorizontal"),
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0, 0.5)
        };
        var layers = new Grid();
        layers.Children.Add(row);
        layers.Children.Add(bar);

        card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Ui.Resource<Brush>("RowBgSoft"),
            BorderBrush = Ui.Frozen(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0),
            Effect = Ui.Resource<System.Windows.Media.Effects.Effect>("PopupShadow"),
            Child = layers
        };
        card.SizeChanged += (_, _) =>
            layers.Clip = new RectangleGeometry(new Rect(0, 0, card.ActualWidth - 2, card.ActualHeight - 2), 13, 13);
        return card;
    }
}

public static class Toasts
{
    public static void Show(Toast toast) =>
        Application.Current?.Dispatcher.InvokeAsync(() => (Application.Current.MainWindow as MainWindow)?.ShowToast(toast));
}

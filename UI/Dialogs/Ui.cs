using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class Ui
{
    public static readonly Brush ProblemBrush = Frozen(Color.FromRgb(0xE3, 0x6D, 0x6F));
    public static readonly Brush WarningBrush = Frozen(Color.FromRgb(0xE3, 0xA6, 0x6D));
    public static readonly Brush GoodBrush = Frozen(Color.FromRgb(0x6F, 0xCF, 0x97));
    public static readonly Brush InfoBrush = Frozen(Color.FromRgb(0x8A, 0x8F, 0x98));

    public static T Resource<T>(string key) => (T)Application.Current.FindResource(key);

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static TextBlock Label(string text) => new() { Text = text, Style = Resource<Style>("FieldLabel") };

    public static TextBlock Note(string text, double bottom = 10, double size = 12) => new()
    {
        Text = text,
        Foreground = Resource<Brush>("SubtleText"),
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom)
    };

    public static TextBlock Problem() => new()
    {
        Foreground = ProblemBrush,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed
    };

    public static bool ShowProblem(TextBlock problem, string? reason, bool visible = true)
    {
        problem.Text = reason ?? "";
        problem.Visibility = reason != null && visible ? Visibility.Visible : Visibility.Collapsed;
        return reason == null;
    }

    public static TextBox Input(string text = "") =>
        new() { Text = text, Style = Resource<Style>("LauncherTextBox"), Margin = new Thickness(0, 0, 0, 12) };

    public static ComboBox Combo(IEnumerable<object> items) => new()
    {
        Style = Resource<Style>("LauncherCombo"),
        ItemsSource = items.ToList(),
        Margin = new Thickness(0, 0, 0, 12)
    };

    public static CheckBox Check(string text, bool isChecked = true) =>
        new() { Content = text, IsChecked = isChecked, Margin = new Thickness(0, 0, 0, 8) };

    public static void SetOption(CheckBox box, string label, bool available)
    {
        box.Content = label;
        box.IsEnabled = available;
        box.IsChecked = available;
    }

    public static bool IsChosen(CheckBox? box) => box is { IsEnabled: true, IsChecked: true };

    public static RadioButton Choice(string text, string group, bool isChecked = false) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = isChecked,
        Style = Resource<Style>("SegmentButton")
    };

    public static Button Button(string text, Action click)
    {
        var button = new Button
        {
            Content = text,
            Style = Resource<Style>("LauncherButton"),
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, 0, 8, 0)
        };
        button.Click += (_, _) => click();
        return button;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    public static StackPanel Stack(params UIElement?[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children.OfType<UIElement>())
            stack.Children.Add(child);
        return stack;
    }

    public static ScrollViewer Scroll(UIElement content, double maxHeight)
    {
        var viewer = new ScrollViewer
        {
            Content = content,
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10)
        };
        viewer.Resources.Add(typeof(ScrollBar), Resource<Style>("SlimScrollBar"));
        return viewer;
    }

    public static void FillPresetChoices(Panel panel, string group, string activeId, RoutedEventHandler onChecked)
    {
        panel.Children.Clear();
        foreach (var preset in JvmPresets.All)
        {
            var button = Choice(preset.Name, group, preset.Id == activeId);
            button.Tag = preset.Id;
            button.Checked += onChecked;
            panel.Children.Add(button);
        }
    }

    public static UIElement IconText(string? icon, string text, double size = 15)
    {
        if (icon == null)
            return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon { Kind = icon, Size = size, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public static Image UrlImage(string? url)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        if (url != null)
        {
            try
            {
                image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(url));
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Bild laden", ex);
            }
        }
        return image;
    }

    public static System.Windows.Shapes.Ellipse Dot(string presence) => new()
    {
        Width = 8,
        Height = 8,
        Margin = new Thickness(0, 0, 7, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Fill = Resource<Brush>(presence switch { "online" => "Good", "ingame" => "Ingame", _ => "Offline" })
    };

    public static FrameworkElement Spinner(double size = 34)
    {
        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 3,
            Stroke = new LinearGradientBrush
            {
                GradientStops = { new GradientStop(Color.FromRgb(0xD3, 0x6A, 0xD8), 0), new GradientStop(Color.FromRgb(0xD3, 0x6A, 0xD8), 0.35), new GradientStop(Color.FromRgb(0x3A, 0x3A, 0x3A), 0.36) }
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform()
        };
        ring.Loaded += (_, _) => ring.RenderTransform.BeginAnimation(RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            });
        return ring;
    }

    public static T DataOf<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    public static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}

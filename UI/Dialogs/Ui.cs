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
        Style = Resource<Style>("SegmentButton"),
        Padding = new Thickness(12, 6, 12, 6)
    };

    public static Button Button(string text, Action click)
    {
        var button = new Button
        {
            Content = text,
            Style = Resource<Style>("LauncherButton"),
            Padding = new Thickness(12, 6, 12, 6),
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

    public static T DataOf<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    public static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}

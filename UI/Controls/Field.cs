using System.Windows;

namespace AxoClient.UI.Controls;

public static class Field
{
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached("Hint", typeof(string),
        typeof(Field), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(string),
        typeof(Field), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached("Count", typeof(string),
        typeof(Field), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty CornerProperty = DependencyProperty.RegisterAttached("Corner",
        typeof(CornerRadius), typeof(Field), new FrameworkPropertyMetadata(new CornerRadius(8)));

    public static CornerRadius GetCorner(DependencyObject o) => (CornerRadius)o.GetValue(CornerProperty);
    public static void SetCorner(DependencyObject o, CornerRadius value) => o.SetValue(CornerProperty, value);

    public static string GetHint(DependencyObject o) => (string)o.GetValue(HintProperty);
    public static void SetHint(DependencyObject o, string value) => o.SetValue(HintProperty, value);

    public static string GetIcon(DependencyObject o) => (string)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string value) => o.SetValue(IconProperty, value);

    public static string GetCount(DependencyObject o) => (string)o.GetValue(CountProperty);
    public static void SetCount(DependencyObject o, string value) => o.SetValue(CountProperty, value);
}

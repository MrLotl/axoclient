using System.Windows;
using System.Windows.Media;

namespace AxoClient.UI.Controls;

public sealed class PingBars : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(int),
        typeof(PingBars), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush[] Colors =
    [
        Ui.Frozen(Color.FromRgb(0x6B, 0x6B, 0x6B)), Ui.Frozen(Color.FromRgb(0xF4, 0x70, 0x67)),
        Ui.Frozen(Color.FromRgb(0xF0, 0x88, 0x3E)), Ui.Frozen(Color.FromRgb(0xE3, 0xB3, 0x41)),
        Ui.Frozen(Color.FromRgb(0x3F, 0xB9, 0x50))
    ];

    private static readonly Brush Off = Ui.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public static Brush BrushFor(int level) => Colors[Math.Clamp(level, 0, 4)];

    protected override Size MeasureOverride(Size availableSize) => new(14, 11);

    protected override void OnRender(DrawingContext dc)
    {
        var on = BrushFor(Level);
        for (var i = 0; i < 4; i++)
        {
            var height = 4 + i * 2.4;
            dc.DrawRoundedRectangle(i < Level ? on : Off, null, new Rect(i * 3.6, 11 - height, 2.2, height), 1, 1);
        }
    }
}

public sealed class PingBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        PingBars.BrushFor(value is int level ? level : 0);

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

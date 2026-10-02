using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Controls;

public sealed class AspectBox : Decorator
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(nameof(Ratio), typeof(double),
        typeof(AspectBox), new FrameworkPropertyMetadata(16.0 / 9, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsInfinity(constraint.Width) ? 160 : constraint.Width;
        var size = new Size(width, width / Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = new Size(arrangeSize.Width, arrangeSize.Width / Ratio);
        Child?.Arrange(new Rect(size));
        return size;
    }
}

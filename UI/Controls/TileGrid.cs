using System.Windows;
using System.Windows.Controls.Primitives;

namespace AxoClient.UI.Controls;

public sealed class TileGrid : UniformGrid
{
    public static readonly DependencyProperty TileWidthProperty = DependencyProperty.Register(nameof(TileWidth), typeof(double),
        typeof(TileGrid), new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double TileWidth
    {
        get => (double)GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!double.IsInfinity(constraint.Width))
        {
            var columns = Math.Max(1, (int)(constraint.Width / TileWidth));
            if (columns != Columns)
                SetCurrentValue(ColumnsProperty, columns);
        }
        return base.MeasureOverride(constraint);
    }
}

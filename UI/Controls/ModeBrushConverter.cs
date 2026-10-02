using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AxoClient.UI.Controls;

public sealed class ModeBrushConverter : IValueConverter
{
    private static readonly Brush Creative = Ui.Frozen(Color.FromArgb(0x8C, 0x4C, 0x8D, 0xFF));
    private static readonly Brush Hardcore = Ui.Frozen(Color.FromArgb(0xA6, 0xC8, 0x28, 0x28));
    private static readonly Brush Normal = Ui.Frozen(Color.FromArgb(0x80, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "Kreativ" => Creative,
        "Hardcore" => Hardcore,
        _ => Normal
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

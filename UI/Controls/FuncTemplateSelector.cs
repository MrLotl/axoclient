using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Controls;

public interface ISpecialItem
{
    bool IsSpecial { get; }
}

public sealed class FuncTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Normal { get; set; }
    public DataTemplate? Special { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ISpecialItem { IsSpecial: true } ? Special : Normal;
}

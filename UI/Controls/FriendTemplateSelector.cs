using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Controls;

public sealed class FriendTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Friend { get; set; }
    public DataTemplate? Outgoing { get; set; }
    public DataTemplate? Incoming { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        FriendInfo { IsIncoming: true } => Incoming,
        FriendInfo { IsOutgoing: true } => Outgoing,
        _ => Friend
    };
}

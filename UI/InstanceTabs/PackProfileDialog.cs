using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public static class PackProfileDialog
{
    public static async Task ShowAsync(AppServices app, Installation inst)
    {
        var panel = new PackProfilePanel { Margin = new Thickness(22, 16, 22, 22) };
        panel.Show(app, inst);
        var header = DialogParts.Header("Paket-Profile", $"{inst.Name} · {InstanceText.Version(inst)}",
            DialogParts.IconTile("Layers"), app.Dialogs.ClosePanel);
        header.Margin = new Thickness(22, 22, 22, 0);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(panel);
        await app.Dialogs.ShowPanelAsync(root, 720);
    }
}

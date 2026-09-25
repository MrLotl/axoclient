using System.Windows.Controls;

namespace AxoClient.UI.Dialogs;

public static class ServerPicker
{
    public static async Task<List<string>?> ChooseAsync(AppServices app, Installation inst, string intro, string emptyText,
        IReadOnlyCollection<string> chosen, Func<string, string?>? ownerOf = null)
    {
        var options = new ServerStore(inst.GameDir).Addresses();
        foreach (var extra in chosen.Where(c => !options.Any(o => o.Address.Equals(c, StringComparison.OrdinalIgnoreCase))))
            options.Add((extra, extra));

        var boxes = new List<(CheckBox Box, string Address)>();
        var list = new StackPanel();
        foreach (var (name, address) in options)
        {
            var label = name.Equals(address, StringComparison.OrdinalIgnoreCase) ? address : $"{name}  ·  {address}";
            var owner = ownerOf?.Invoke(address);
            var box = Ui.Check(owner != null ? $"{label}  (gehört zu \"{owner}\")" : label,
                chosen.Any(c => c.Equals(address, StringComparison.OrdinalIgnoreCase)));
            box.IsEnabled = owner == null;
            boxes.Add((box, address));
            list.Children.Add(box);
        }
        if (boxes.Count == 0)
            list.Children.Add(Ui.Note(emptyText, 0, 11));

        var form = Ui.Stack(Ui.Note(intro), Ui.Scroll(list, 260));
        if (!await app.Dialogs.ShowFormAsync("Server für das Profil", form, "Speichern"))
            return null;
        return boxes.Where(b => Ui.IsChosen(b.Box)).Select(b => b.Address).ToList();
    }
}

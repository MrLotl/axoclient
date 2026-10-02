using System.Windows;

namespace AxoClient.UI.Dialogs;

public static class ServerForm
{
    public static async Task<(string Name, string Address)?> AskAsync(AppServices app, string title, string subtitle,
        string submit, string name = "", string address = "")
    {
        var nameBox = Ui.Input(name);
        Field.SetHint(nameBox, "z. B. Mein SMP");
        nameBox.Margin = new Thickness(0, 0, 0, 16);
        var addressBox = Ui.Input(address);
        Field.SetHint(addressBox, "play.beispiel.de oder 127.0.0.1:25565");
        Field.SetIcon(addressBox, "Globe");
        addressBox.Margin = new Thickness(0);
        var form = Ui.Stack(Ui.Label("Name"), nameBox, Ui.Label("Server-Adresse"), addressBox);
        if (!await app.Dialogs.ShowFormAsync(title, form, submit, () => addressBox.Text.Trim().Length > 0, 440, subtitle,
                "Server"))
            return null;
        var cleanAddress = addressBox.Text.Trim();
        var cleanName = nameBox.Text.Trim();
        return (cleanName.Length > 0 ? cleanName : cleanAddress, cleanAddress);
    }
}

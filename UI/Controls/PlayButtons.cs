using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Controls;

public static class PlayButtons
{
    public const string PlayText = "Spielen";
    public const string StopText = "■  Beenden";

    public static void Apply(Button button, bool running)
    {
        button.Content = running ? StopText : PlayText;
        button.Background = Ui.Resource<Brush>(running ? "Danger" : "Accent");
        button.ToolTip = running ? "Minecraft beenden" : null;
    }

    public static async Task PlayOrStopAsync(AppServices app, Installation inst, Action play)
    {
        if (!app.Games.IsRunning(inst))
        {
            play();
            return;
        }
        if (!await app.Dialogs.ConfirmAsync("Minecraft beenden",
                $"\"{inst.Name}\" sofort beenden?\n\nMinecraft speichert Welten alle paar Minuten automatisch; " +
                "was seitdem passiert ist, kann verloren gehen.", "Beenden", danger: true))
            return;
        try
        {
            app.Games.Stop(inst);
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowErrorAsync("Minecraft konnte nicht beendet werden", ex,
                "Du kannst das Spiel auch über den Task-Manager beenden.");
        }
    }
}

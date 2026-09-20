using System.Windows;

namespace McLauncher.Pages;

public partial class HomePage
{
    /// <summary>Nach einem Absturz gleich anbieten, die Ursache zu suchen.</summary>
    private async void OnGameCrashed(Installation inst)
    {
        if (await _app.Dialogs.ConfirmAsync("Minecraft ist abgestürzt",
                $"\"{inst.Name}\" wurde mit einem Fehler beendet. Soll der Launcher die Ursache suchen und Maßnahmen vorschlagen?",
                "Analysieren"))
            await CrashUi.ShowAsync(_app, inst, justCrashed: true);
    }
}

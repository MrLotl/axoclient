using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>"Spielen"-Knöpfe werden rot mit Stopp-Quadrat, solange die Instanz läuft.</summary>
public static class PlayButtons
{
    public const string PlayText = "Spielen";
    public const string StopText = "■  Beenden";

    public static void Apply(Button button, bool running)
    {
        button.Content = running ? StopText : PlayText;
        button.Background = (System.Windows.Media.Brush)Application.Current.FindResource(running ? "Danger" : "Accent");
        button.ToolTip = running ? "Minecraft beenden" : null;
    }
}

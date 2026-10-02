using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Controls;

public class InstanceItem(Installation installation, bool running)
{
    public Installation Installation { get; } = installation;
    public bool IsRunning { get; } = running;
    public string Name => Installation.Name;
    public string VersionLabel => InstanceText.Version(Installation);
    public string LoaderInitial => Installation.LoaderInitial;
    public BitmapSource? Image { get; } = InstanceIcons.Load(installation);
    public bool HasImage => Image != null;
    public string PlayLabel => IsRunning ? "Beenden" : "Starten";
    public string PlayIcon => IsRunning ? "Stop" : "Play";
    public Brush Placeholder => InstanceText.Placeholder(Installation);
}

public static class InstanceText
{
    public static string Version(Installation inst) =>
        inst.Loader == LoaderType.Vanilla ? $"{inst.MinecraftVersion} · Vanilla" : $"{inst.MinecraftVersion} · {inst.Loader}";

    public static Brush Placeholder(Installation inst)
    {
        var hue = inst.Id.Sum(c => c) % 3;
        var (a, b) = hue switch
        {
            0 => (Color.FromRgb(0x4A, 0x26, 0x47), Color.FromRgb(0x2A, 0x2A, 0x3E)),
            1 => (Color.FromRgb(0x26, 0x3B, 0x2C), Color.FromRgb(0x2A, 0x2F, 0x3E)),
            _ => (Color.FromRgb(0x3B, 0x2A, 0x4E), Color.FromRgb(0x4A, 0x26, 0x3A))
        };
        var brush = new LinearGradientBrush(a, b, 45);
        brush.Freeze();
        return brush;
    }
}

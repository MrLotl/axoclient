using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Controls;

public class ContentRow : Observable
{
    private bool _active;

    public ContentRow(InstalledItem item, bool active)
    {
        Item = item;
        _active = active;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstalledItem.Icon))
                Changed(nameof(Icon));
            if (e.PropertyName is nameof(InstalledItem.Update) or nameof(InstalledItem.HasUpdate))
                Changed(nameof(HasUpdate));
        };
    }

    public InstalledItem Item { get; }
    public string Name => Item.DisplayName;
    public string Version => Item.Entry?.VersionName ?? Item.FileName;
    public string Description => Item.Entry == null ? "Manuell hinzugefügt · " + Item.FileName : Item.SourceText;
    public BitmapSource? Icon => Item.Icon;
    public string Letter => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public Brush LetterBrush => Letters.BrushFor(Name);
    public bool HasUpdate => Item.HasUpdate;
    public bool IsShader => Item.Type == ContentType.Shader;
    public bool CanToggle => !IsShader;
    public bool CanShare => Item.CanShare;
    public bool CanChangeVersion => Item.CanChangeVersion;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Changed(nameof(Active), nameof(Dimmed), nameof(ShowActivate), nameof(ShowActiveBadge));
        }
    }

    public double Dimmed => IsShader || Active ? 1 : 0.55;
    public bool ShowActivate => IsShader && !Active;
    public bool ShowActiveBadge => IsShader && Active;
}

public static class Letters
{
    private static readonly (Color A, Color B)[] Palette =
    [
        (Color.FromRgb(0xB4, 0x53, 0x2A), Color.FromRgb(0x7C, 0x2D, 0x12)),
        (Color.FromRgb(0x2F, 0x9E, 0x44), Color.FromRgb(0x5C, 0x94, 0x0D)),
        (Color.FromRgb(0x74, 0xC0, 0xFC), Color.FromRgb(0x4D, 0x7B, 0xF7)),
        (Color.FromRgb(0xDB, 0x27, 0x77), Color.FromRgb(0x7C, 0x3A, 0xED)),
        (Color.FromRgb(0xF0, 0x8C, 0x00), Color.FromRgb(0xE8, 0x59, 0x0C)),
        (Color.FromRgb(0x66, 0x5C, 0xE6), Color.FromRgb(0x34, 0x3A, 0x9E))
    ];

    public static Brush BrushFor(string name)
    {
        var (a, b) = Palette[name.Sum(c => c) % Palette.Length];
        var brush = new LinearGradientBrush(a, b, 45);
        brush.Freeze();
        return brush;
    }
}

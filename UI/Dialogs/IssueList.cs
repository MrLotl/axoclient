using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public sealed class IssueList
{
    private readonly List<(CheckBox Box, Issue Issue)> _boxes = [];

    public IssueList(IEnumerable<Issue> issues, bool withMarkers)
    {
        var list = new StackPanel();
        foreach (var issue in issues)
            list.Children.Add(Entry(issue, withMarkers));
        View = list;
    }

    public UIElement View { get; }

    public bool AnyFixable => _boxes.Count > 0;

    public List<Issue> Chosen => _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Issue).ToList();

    private UIElement Entry(Issue issue, bool withMarker)
    {
        var (icon, color) = issue.Severity switch
        {
            IssueSeverity.Error => ("Warning", Color.FromRgb(0xF4, 0x70, 0x67)),
            IssueSeverity.Warning => ("Warning", Color.FromRgb(0xE3, 0xB3, 0x41)),
            _ => ("Info", Color.FromRgb(0x8A, 0x8F, 0x98))
        };

        var row = new DockPanel();
        if (withMarker)
        {
            var tile = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = Ui.Frozen(Color.FromArgb(0x24, color.R, color.G, color.B)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0),
                Child = new Icon
                {
                    Kind = icon,
                    Size = 15,
                    Foreground = Ui.Frozen(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            DockPanel.SetDock(tile, Dock.Left);
            row.Children.Add(tile);
        }

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock
        {
            Text = issue.Title,
            Foreground = Ui.Resource<Brush>("TextStrong"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(issue.Description))
            texts.Children.Add(new TextBlock
            {
                Text = issue.Description,
                Foreground = Ui.Resource<Brush>("MutedText"),
                FontSize = 12.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
        if (issue.CanFix)
        {
            var box = Ui.Check(issue.FixText ?? "Beheben", issue.Selected);
            box.Margin = new Thickness(0, 10, 0, 0);
            _boxes.Add((box, issue));
            texts.Children.Add(box);
        }
        row.Children.Add(texts);

        return new Border
        {
            Background = Ui.Resource<Brush>("RowBg"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row
        };
    }
}

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
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, withMarker ? 12 : 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        if (withMarker)
            head.Children.Add(new TextBlock
            {
                Text = issue.Marker,
                Foreground = issue.Severity switch
                {
                    IssueSeverity.Error => Ui.ProblemBrush,
                    IssueSeverity.Warning => Ui.WarningBrush,
                    _ => Ui.InfoBrush
                },
                FontSize = 12,
                Margin = new Thickness(0, 1, 8, 0)
            });
        head.Children.Add(new TextBlock
        {
            Text = issue.Title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        });
        panel.Children.Add(head);

        var indent = withMarker ? 20 : 0;
        var detail = Ui.Note(issue.Description, withMarker ? 0 : 6, 12);
        detail.Margin = new Thickness(indent, 0, 0, withMarker ? 0 : 6);
        panel.Children.Add(detail);

        if (issue.CanFix)
        {
            var box = Ui.Check(issue.FixText ?? "Beheben", issue.Selected);
            box.Margin = new Thickness(indent, withMarker ? 6 : 0, 0, withMarker ? 0 : 14);
            _boxes.Add((box, issue));
            panel.Children.Add(box);
        }
        return panel;
    }
}

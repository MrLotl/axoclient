using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AxoClient.UI.Pages;

public sealed class LogLine
{
    private static readonly Regex Vanilla = new(@"^\[(\d\d:\d\d:\d\d)\] \[([^\]]+?)/(\w+)\]: ?(.*)$", RegexOptions.Compiled);
    private static readonly Regex Paper = new(@"^\[(\d\d:\d\d:\d\d) (\w+)\]:? ?(.*)$", RegexOptions.Compiled);
    private static readonly Brush InfoLevel = Ui.Frozen(Color.FromRgb(0x7A, 0xA2, 0xF7));
    private static readonly Brush WarnLevel = Ui.Frozen(Color.FromRgb(0xE3, 0xB3, 0x41));
    private static readonly Brush ErrorLevel = Ui.Frozen(Color.FromRgb(0xF4, 0x70, 0x67));
    private static readonly Brush InfoText = Ui.Frozen(Color.FromRgb(0xD4, 0xD4, 0xD4));
    private static readonly Brush WarnText = Ui.Frozen(Color.FromRgb(0xF0, 0xD5, 0x8A));
    private static readonly Brush ErrorText = Ui.Frozen(Color.FromRgb(0xFF, 0xB4, 0xAE));
    private static readonly Brush WarnRow = Ui.Frozen(Color.FromArgb(0x0D, 0xE3, 0xB3, 0x41));
    private static readonly Brush ErrorRow = Ui.Frozen(Color.FromArgb(0x12, 0xF4, 0x70, 0x67));

    public LogLine(string text, int lastLevel)
    {
        Text = text;
        string? level = null;
        Message = text;
        if (Vanilla.Match(text) is { Success: true } v)
        {
            Time = v.Groups[1].Value;
            Thread = v.Groups[2].Value;
            level = v.Groups[3].Value;
            Message = v.Groups[4].Value;
        }
        else if (Paper.Match(text) is { Success: true } p)
        {
            Time = p.Groups[1].Value;
            level = p.Groups[2].Value;
            Message = p.Groups[3].Value;
        }

        Severity = level switch
        {
            "WARN" or "WARNING" => 1,
            "ERROR" or "FATAL" or "SEVERE" => 2,
            null when text.Length > 0 && (char.IsWhiteSpace(text[0]) || text.StartsWith("Caused by") ||
                                          text.StartsWith("at ") || text.StartsWith("...")) => lastLevel,
            _ => 0
        };
        Level = level switch { null => "", "WARNING" => "WARN", "SEVERE" => "ERROR", _ => level };
        LevelBrush = Severity switch { 1 => WarnLevel, 2 => ErrorLevel, _ => InfoLevel };
        MessageBrush = Severity switch { 1 => WarnText, 2 => ErrorText, _ => InfoText };
        RowBrush = Severity switch { 1 => WarnRow, 2 => ErrorRow, _ => Brushes.Transparent };
        MarkBrush = Severity == 2 ? ErrorLevel : Brushes.Transparent;
    }

    public string Text { get; }
    public string Time { get; } = "";
    public string Level { get; }
    public string Thread { get; } = "";
    public string Message { get; }
    public int Severity { get; }
    public Brush LevelBrush { get; }
    public Brush MessageBrush { get; }
    public Brush RowBrush { get; }
    public Brush MarkBrush { get; }
}

public sealed record LogSource(Installation? Inst, LocalServer? Server)
{
    public string Id => Inst?.Id ?? "srv:" + Server!.Id;
    public string Name => Inst?.Name ?? Server!.Name;
    public string Kind => Inst != null ? "Instanz" : "Server";
    public string Type => Inst != null ? "" : " · " + Server!.TypeLabel;
    public string LatestLog => Inst?.LatestLog ?? Server!.LatestLog;
    public string LogsDir => Inst?.LogsDir ?? Server!.LogsDir;
    public string RootDir => Inst?.GameDir ?? Server!.Dir;
}

public partial class GameLogPage : UserControl
{
    private const int InitialBytes = 2 * 1024 * 1024;
    private const int MaxLines = 20000, TrimTo = 15000;

    private AppServices _app = null!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private readonly List<LogLine> _all = [];
    private ObservableCollection<LogLine> _shown = [];

    private LogSource? _source;
    private string? _path;
    private long _offset;
    private byte[] _head = [];
    private Decoder _decoder = Encoding.UTF8.GetDecoder();
    private string _partial = "";
    private int _lastLevel;
    private bool _polling;
    private bool _reloadRequested;
    private bool _cleared;
    private HashSet<string> _runningIds = [];

    public GameLogPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _shown;
        _timer.Tick += async (_, _) => await PollAsync();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _timer.Stop();
                return;
            }
            RefreshSources(preferRunning: true);
            _timer.Start();
            _ = PollAsync();
        };
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Instances.Changed += () => Dispatcher.InvokeAsync(() => RefreshSources(false));
        _app.Games.Changed += () => Dispatcher.InvokeAsync(() => RefreshSources(false));
        _app.LocalServers.Changed += () => Dispatcher.InvokeAsync(() => RefreshSources(false));
        RefreshSources(preferRunning: true);
    }

    public void ShowServer(LocalServer server) => SwitchTo(new LogSource(null, server));

    private List<LogSource> Sources() =>
        _app.Instances.All.Select(i => new LogSource(i, null))
            .Concat(_app.LocalServers.Servers.Select(s => new LogSource(null, s)))
            .ToList();

    private bool IsRunning(LogSource source) =>
        source.Inst != null ? _app.Games.IsRunning(source.Inst) : _app.LocalServers.IsRunning(source.Server!);

    private void RefreshSources(bool preferRunning)
    {
        if (_app == null)
            return;
        var sources = Sources();
        var running = sources.Where(IsRunning).Select(s => s.Id).ToHashSet();
        var justStarted = sources.FirstOrDefault(s => s.Inst != null && running.Contains(s.Id) && !_runningIds.Contains(s.Id));
        _runningIds = running;

        var currentId = _source?.Id;
        var pick = justStarted
                   ?? (preferRunning && !running.Contains(currentId ?? "")
                       ? sources.FirstOrDefault(s => running.Contains(s.Id) && s.Inst == _app.Instances.Selected)
                         ?? sources.FirstOrDefault(s => running.Contains(s.Id))
                       : null)
                   ?? sources.FirstOrDefault(s => s.Id == currentId)
                   ?? sources.FirstOrDefault(s => s.Inst != null && s.Inst == _app.Instances.Selected)
                   ?? sources.FirstOrDefault();

        if (pick?.Id != _source?.Id)
            SwitchTo(pick);
        else
            UpdateHeader();
    }

    private void SourceButton_Checked(object sender, RoutedEventArgs e)
    {
        BuildSourceList();
        SourcePopup.IsOpen = true;
    }

    private void SourcePopup_Closed(object? sender, EventArgs e) => SourceButton.IsChecked = false;

    private void BuildSourceList()
    {
        SourceList.Children.Clear();
        var sources = Sources();
        AddGroup("Instanzen", sources.Where(s => s.Inst != null));
        AddGroup("Lokale Server", sources.Where(s => s.Server != null));
    }

    private void AddGroup(string title, IEnumerable<LogSource> sources)
    {
        var list = sources.ToList();
        if (list.Count == 0)
            return;
        SourceList.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Resource<Brush>("LabelText"), Margin = new Thickness(10, SourceList.Children.Count == 0 ? 6 : 12, 10, 6)
        });
        foreach (var source in list)
        {
            var running = IsRunning(source);
            var row = new DockPanel();
            var status = new TextBlock
            {
                Text = running ? "Läuft" : "Gestoppt", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
                Foreground = running ? Ui.Frozen(Color.FromRgb(0x6F, 0xDC, 0x80)) : Ui.Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A))
            };
            DockPanel.SetDock(status, Dock.Right);
            row.Children.Add(status);
            row.Children.Add(Dot(running));
            row.Children.Add(new TextBlock
            {
                Text = source.Name + source.Type, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0),
                FontWeight = source.Id == _source?.Id ? FontWeights.SemiBold : FontWeights.Normal
            });
            var button = new Button { Style = Ui.Resource<Style>("MenuItemButton"), Content = row };
            button.Click += (_, _) =>
            {
                SourcePopup.IsOpen = false;
                SwitchTo(source);
            };
            SourceList.Children.Add(button);
        }
    }

    private static Ellipse Dot(bool running) => new()
    {
        Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
        Fill = running ? Ui.Resource<Brush>("Good") : Ui.Frozen(Color.FromRgb(0x6B, 0x6B, 0x6B)),
        Effect = running ? new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0x3F, 0xB9, 0x50), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8
        } : null
    };

    private void SwitchTo(LogSource? source)
    {
        _source = source;
        _path = null;
        _reloadRequested = true;
        _cleared = false;
        _all.Clear();
        _shown = [];
        LogList.ItemsSource = _shown;
        FollowCheck.IsChecked = true;
        CommandBox.Text = "";
        UpdateHeader();
        UpdateFooter();
        if (IsVisible)
            _ = PollAsync();
    }

    private void UpdateHeader()
    {
        if (_source == null)
        {
            SourceStatus.Text = "";
            SourceKind.Text = "Keine Quelle";
            SourceName.Text = "";
            SourceType.Text = "";
            SourceDot.Fill = Ui.Resource<Brush>("Offline");
            CommandBar.Visibility = Visibility.Collapsed;
            return;
        }
        var running = IsRunning(_source);
        var dot = Dot(running);
        SourceDot.Fill = dot.Fill;
        SourceDot.Effect = dot.Effect;
        SourceStatus.Text = running ? "Läuft" : "Gestoppt";
        SourceStatus.Foreground = running ? Ui.Frozen(Color.FromRgb(0x6F, 0xDC, 0x80)) : Ui.Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));
        SourceKind.Text = _source.Kind;
        SourceName.Text = _source.Name;
        SourceType.Text = _source.Type;
        CommandBar.Visibility = _source.Server != null && running ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record Chunk(bool Reset, List<LogLine> Lines, bool Missing);

    private async Task PollAsync()
    {
        if (_polling || _source is not { } source)
            return;
        _polling = true;
        try
        {
            var path = source.LatestLog;
            var chunk = await Task.Run(() => Read(path));
            if (source != _source)
                return;
            Apply(chunk);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Log lesen", ex);
            PathText.Text = "Das Log ließ sich gerade nicht lesen: " + ErrorReport.Short(ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private Chunk Read(string path)
    {
        if (!File.Exists(path))
        {
            var wasShowing = _path != null || _reloadRequested;
            _path = null;
            _reloadRequested = false;
            return new Chunk(wasShowing, [], true);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;

        var head = new byte[(int)Math.Min(128, length)];
        stream.ReadExactly(head);
        var reset = _reloadRequested || _path != path || length < _offset ||
                    !head.AsSpan(0, Math.Min(head.Length, _head.Length))
                        .SequenceEqual(_head.AsSpan(0, Math.Min(head.Length, _head.Length)));
        if (reset)
        {
            _reloadRequested = false;
            _path = path;
            _head = head;
            _decoder = Encoding.UTF8.GetDecoder();
            _partial = "";
            _lastLevel = 0;
            _offset = Math.Max(0, length - InitialBytes);
        }
        else if (_head.Length < head.Length)
        {
            _head = head;
        }

        if (length == _offset)
            return new Chunk(reset, [], false);

        var skipFirstLine = reset && _offset > 0;
        stream.Seek(_offset, SeekOrigin.Begin);
        var bytes = new byte[length - _offset];
        var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        _offset += read;

        var chars = new char[_decoder.GetCharCount(bytes, 0, read)];
        _decoder.GetChars(bytes, 0, read, chars, 0);
        var text = _partial + new string(chars);

        var lastBreak = text.LastIndexOf('\n');
        _partial = text[(lastBreak + 1)..];
        var lines = new List<LogLine>();
        if (lastBreak < 0)
            return new Chunk(reset, lines, false);

        var first = true;
        foreach (var raw in text[..lastBreak].Split('\n'))
        {
            if (first && skipFirstLine)
            {
                first = false;
                continue;
            }
            first = false;
            var line = new LogLine(raw.TrimEnd('\r'), _lastLevel);
            _lastLevel = line.Severity;
            lines.Add(line);
        }
        return new Chunk(reset, lines, false);
    }

    private void Apply(Chunk chunk)
    {
        if (chunk.Reset)
        {
            _all.Clear();
            _cleared = false;
        }
        _all.AddRange(chunk.Lines);

        var rebuild = chunk.Reset;
        if (_all.Count > MaxLines)
        {
            _all.RemoveRange(0, _all.Count - TrimTo);
            rebuild = true;
        }

        if (rebuild)
        {
            _shown = new ObservableCollection<LogLine>(_all.Where(Matches));
            LogList.ItemsSource = _shown;
        }
        else
        {
            foreach (var line in chunk.Lines.Where(Matches))
                _shown.Add(line);
        }

        UpdateEmpty(chunk.Missing);
        if ((rebuild || chunk.Lines.Count > 0) && FollowCheck.IsChecked == true)
            ScrollToEnd();
        UpdateHeader();
        UpdateFooter();
    }

    private void UpdateEmpty(bool missing = false)
    {
        EmptyText.Text = missing
            ? $"Für „{_source?.Name}“ gibt es noch kein Log.\nStarte {(_source?.Server != null ? "den Server" : "die Instanz")}, dann erscheinen die Zeilen hier live."
            : _cleared && _all.Count == 0 ? "Konsole geleert – neue Einträge erscheinen hier automatisch."
            : _all.Count > 0 ? "Keine Einträge für diesen Filter."
            : _source == null ? "Noch keine Instanz oder kein Server vorhanden." : "Das Log ist noch leer.";
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFooter()
    {
        CountAll.Text = _all.Count.ToString();
        CountWarn.Text = _all.Count(l => l.Severity == 1).ToString();
        CountError.Text = _all.Count(l => l.Severity == 2).ToString();
        ShownText.Text = $"{_shown.Count} von {_all.Count} Einträgen";
        PathText.Text = _path ?? _source?.LatestLog ?? "";
        FollowLabel.Text = FollowCheck.IsChecked == true ? "Mitlaufen aktiv" : "Mitlaufen pausiert";
    }

    private bool Matches(LogLine line)
    {
        var minLevel = LevelError.IsChecked == true ? 2 : LevelWarn.IsChecked == true ? 1 : 0;
        if (minLevel > 0 && line.Severity != minLevel)
            return false;
        var filter = FilterBox.Text;
        return filter.Length == 0 || line.Text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildShown()
    {
        if (LogList == null || FollowCheck == null)
            return;
        _shown = new ObservableCollection<LogLine>(_all.Where(Matches));
        LogList.ItemsSource = _shown;
        UpdateEmpty();
        UpdateFooter();
        if (FollowCheck.IsChecked == true)
            ScrollToEnd();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildShown();

    private void Level_Checked(object sender, RoutedEventArgs e) => RebuildShown();

    private ScrollViewer? _scroller;

    private ScrollViewer? Scroller => _scroller ??= FindScrollViewer(LogList);

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer)
                return viewer;
            if (FindScrollViewer(child) is { } found)
                return found;
        }
        return null;
    }

    private bool _autoScrolling;

    private void ScrollToEnd() => Dispatcher.BeginInvoke(() =>
    {
        _autoScrolling = true;
        Scroller?.ScrollToEnd();
        _autoScrolling = false;
    }, DispatcherPriority.Background);

    private void LogList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_autoScrolling || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 || e.VerticalChange == 0)
            return;
        FollowCheck.IsChecked = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 4;
        UpdateFooter();
    }

    private void Follow_Click(object sender, RoutedEventArgs e)
    {
        if (FollowCheck.IsChecked == true)
            ScrollToEnd();
        UpdateFooter();
    }

    private int _dragAnchor = -1;
    private (int From, int To) _dragRange = (-1, -1);

    private int IndexAt(Point pos)
    {
        pos.Y = Math.Clamp(pos.Y, 1, Math.Max(1, LogList.ActualHeight - 1));
        pos.X = Math.Clamp(pos.X, 1, Math.Max(1, LogList.ActualWidth - 20));
        var hit = LogList.InputHitTest(pos) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);
        return hit is ListBoxItem item ? LogList.ItemContainerGenerator.IndexFromContainer(item) : -1;
    }

    private void LogList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        var index = IndexAt(e.GetPosition(LogList));
        if (index < 0)
            return;
        _dragAnchor = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && LogList.SelectedIndex >= 0
            ? LogList.SelectedIndex
            : index;
        _dragRange = (-1, -1);
        FollowCheck.IsChecked = false;
        UpdateFooter();
    }

    private void LogList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragAnchor < 0)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }
        if (!LogList.IsMouseCaptured)
            LogList.CaptureMouse();

        var pos = e.GetPosition(LogList);
        if (pos.Y < 0)
            Scroller?.LineUp();
        else if (pos.Y > LogList.ActualHeight)
            Scroller?.LineDown();

        var index = IndexAt(pos);
        if (index < 0 || _dragAnchor >= _shown.Count)
            return;
        var range = (From: Math.Min(_dragAnchor, index), To: Math.Max(_dragAnchor, index));
        if (range == _dragRange)
            return;

        var old = _dragRange;
        _dragRange = range;
        if (old.From < 0)
        {
            LogList.SelectedItems.Clear();
            old = (_dragAnchor, _dragAnchor);
            LogList.SelectedItems.Add(_shown[_dragAnchor]);
        }
        for (var i = old.From; i <= old.To; i++)
            if (i < range.From || i > range.To)
                LogList.SelectedItems.Remove(_shown[i]);
        for (var i = range.From; i <= range.To; i++)
            if (i < old.From || i > old.To)
                LogList.SelectedItems.Add(_shown[i]);
    }

    private void LogList_PreviewMouseUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragAnchor = -1;
        _dragRange = (-1, -1);
        if (LogList.IsMouseCaptured)
            LogList.ReleaseMouseCapture();
    }

    private void LogList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopyLines();
            e.Handled = true;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyLines();

    private async void CopyLines()
    {
        var selected = new HashSet<object>(LogList.SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
        IEnumerable<LogLine> lines = selected.Count > 0 ? _shown.Where(selected.Contains) : _shown;
        var text = string.Join(Environment.NewLine, lines.Select(l => l.Text));
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Log kopieren", ex);
            return;
        }
        CopyText.Text = "Kopiert!";
        await Task.Delay(1400);
        CopyText.Text = "Kopieren";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null)
            return;
        Shell.OpenFolder(Directory.Exists(_source.LogsDir) ? _source.LogsDir : _source.RootDir);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _all.Clear();
        _shown = [];
        LogList.ItemsSource = _shown;
        _cleared = true;
        FollowCheck.IsChecked = true;
        UpdateEmpty();
        UpdateFooter();
    }

    private void SendCommand_Click(object sender, RoutedEventArgs e) => SendCommand();

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        SendCommand();
        e.Handled = true;
    }

    private void SendCommand()
    {
        var command = CommandBox.Text.Trim().TrimStart('/');
        if (command.Length == 0 || _source?.Server is not { } server || !_app.LocalServers.IsRunning(server))
            return;
        _app.LocalServers.SendCommand(server, command);
        CommandBox.Text = "";
        FollowCheck.IsChecked = true;
        ScrollToEnd();
    }
}

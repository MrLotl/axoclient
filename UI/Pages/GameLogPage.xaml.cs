using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AxoClient.UI.Pages;

public sealed class LogLine(string text, int level, Brush brush)
{
    public string Text { get; } = text;
    public int Level { get; } = level;
    public Brush Brush { get; } = brush;
}

public partial class GameLogPage : UserControl
{
    private const int InitialBytes = 2 * 1024 * 1024;
    private const int MaxLines = 20000, TrimTo = 15000;

    private static readonly Brush InfoBrush = Frozen(0xD0, 0xD4, 0xD9);
    private static readonly Brush WarnBrush = Frozen(0xE5, 0xC0, 0x7B);
    private static readonly Brush ErrorBrush = Frozen(0xE3, 0x6D, 0x6F);

    private static readonly Regex LevelPattern = new(@"/(INFO|WARN|ERROR|FATAL|DEBUG|TRACE)\]", RegexOptions.Compiled);

    private AppServices _app = null!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private readonly List<LogLine> _all = [];
    private ObservableCollection<LogLine> _shown = [];

    private Installation? _inst;
    private string? _path;
    private long _offset;
    private byte[] _head = [];
    private Decoder _decoder = Encoding.UTF8.GetDecoder();
    private string _partial = "";
    private int _lastLevel;
    private bool _polling;
    private bool _reloadRequested;

    private bool _fillingBox;
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
            FillInstanceBox(preferRunning: true);
            _timer.Start();
            _ = PollAsync();
        };
    }

    public void Initialize(AppServices app)
    {
        _app = app;
        _app.Instances.Changed += () => FillInstanceBox(preferRunning: false);
        _app.Games.Changed += () => Dispatcher.InvokeAsync(() => FillInstanceBox(preferRunning: false));
        FillInstanceBox(preferRunning: true);
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record LogSource(Installation Inst, bool Running)
    {
        public override string ToString() => Running ? Inst.Name + "  ·  läuft" : Inst.Name;
    }

    private void FillInstanceBox(bool preferRunning)
    {
        if (_app == null)
            return;
        var sources = _app.Instances.All.Select(i => new LogSource(i, _app.Games.IsRunning(i))).ToList();

        var running = sources.Where(s => s.Running).Select(s => s.Inst.Id).ToHashSet();
        var justStarted = sources.FirstOrDefault(s => s.Running && !_runningIds.Contains(s.Inst.Id));
        _runningIds = running;

        var currentId = (InstanceBox.SelectedItem as LogSource)?.Inst.Id;
        var pick = justStarted
                   ?? (preferRunning && !running.Contains(currentId ?? "")
                       ? sources.FirstOrDefault(s => s.Running && s.Inst == _app.Instances.Selected)
                         ?? sources.FirstOrDefault(s => s.Running)
                       : null)
                   ?? sources.FirstOrDefault(s => s.Inst.Id == currentId)
                   ?? sources.FirstOrDefault(s => s.Inst == _app.Instances.Selected)
                   ?? sources.FirstOrDefault();

        _fillingBox = true;
        InstanceBox.ItemsSource = sources;
        InstanceBox.SelectedItem = pick;
        _fillingBox = false;
        if (pick?.Inst != _inst)
            SwitchTo(pick?.Inst);
        else
            UpdateStatus();
    }

    private void InstanceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_fillingBox && InstanceBox.SelectedItem is LogSource source)
            SwitchTo(source.Inst);
    }

    private void SwitchTo(Installation? inst)
    {
        _inst = inst;
        _path = null;
        _reloadRequested = true;
        _all.Clear();
        _shown = [];
        LogList.ItemsSource = _shown;
        FollowCheck.IsChecked = true;
        UpdateStatus();
        if (IsVisible)
            _ = PollAsync();
    }

    private sealed record Chunk(bool Reset, List<LogLine> Lines, string? Missing);

    private async Task PollAsync()
    {
        if (_polling || _inst is not { } inst)
            return;
        _polling = true;
        try
        {
            var path = inst.LatestLog;
            var chunk = await Task.Run(() => Read(path));
            if (inst != _inst)
                return;
            Apply(chunk);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Game Log lesen", ex);
            StatusText.Text = "Das Log ließ sich gerade nicht lesen: " + ErrorReport.Short(ex);
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
            return new Chunk(wasShowing, [], path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
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
            return new Chunk(reset, [], null);

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
            return new Chunk(reset, lines, null);

        var first = true;
        foreach (var raw in text[..lastBreak].Split('\n'))
        {
            if (first && skipFirstLine)
            {
                first = false;
                continue;
            }
            first = false;
            lines.Add(Classify(raw.TrimEnd('\r')));
        }
        return new Chunk(reset, lines, null);
    }

    private LogLine Classify(string line)
    {
        var match = LevelPattern.Match(line);
        int level;
        if (match.Success)
            level = match.Groups[1].Value switch { "WARN" => 1, "ERROR" or "FATAL" => 2, _ => 0 };
        else if (line.Length > 0 && (char.IsWhiteSpace(line[0]) || line.StartsWith("Caused by") ||
                                     line.StartsWith("at ") || line.StartsWith("...")))
            level = _lastLevel;
        else
            level = 0;
        _lastLevel = level;
        return new LogLine(line, level, level switch { 1 => WarnBrush, 2 => ErrorBrush, _ => InfoBrush });
    }

    private void Apply(Chunk chunk)
    {
        if (chunk.Reset)
            _all.Clear();
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

        if (chunk.Missing != null)
            EmptyText.Text = $"Für „{_inst?.Name}“ gibt es noch kein Log.\nStarte die Instanz, dann erscheinen die Zeilen hier live.";
        else
            EmptyText.Text = _all.Count > 0 && _shown.Count == 0 ? "Keine Zeile passt zur Suche." : "Das Log ist noch leer.";
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if ((rebuild || chunk.Lines.Count > 0) && FollowCheck.IsChecked == true)
            ScrollToEnd();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_inst == null)
        {
            StatusText.Text = "Keine Instanz vorhanden.";
            return;
        }
        var warnings = _all.Count(l => l.Level == 1);
        var errors = _all.Count(l => l.Level == 2);
        var state = _app.Games.IsRunning(_inst) ? "läuft" : "beendet";
        StatusText.Text = _path == null
            ? _inst.LatestLog
            : $"{_path}  ·  {_all.Count} Zeilen, {warnings} Warnungen, {errors} Fehler  ·  Spiel {state}";
    }

    private bool Matches(LogLine line)
    {
        var minLevel = LevelError.IsChecked == true ? 2 : LevelWarn.IsChecked == true ? 1 : 0;
        if (line.Level < minLevel)
            return false;
        var filter = FilterBox.Text;
        return filter.Length == 0 || line.Text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildShown()
    {
        if (LogList == null)
            return;
        _shown = new ObservableCollection<LogLine>(_all.Where(Matches));
        LogList.ItemsSource = _shown;
        EmptyText.Text = _all.Count > 0 ? "Keine Zeile passt zur Suche." : EmptyText.Text;
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
    }

    private void Follow_Click(object sender, RoutedEventArgs e)
    {
        if (FollowCheck.IsChecked == true)
            ScrollToEnd();
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

    private void CopyLines()
    {
        var selected = new HashSet<object>(LogList.SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
        IEnumerable<LogLine> lines = selected.Count > 0 ? _shown.Where(selected.Contains) : _shown;
        var text = string.Join(Environment.NewLine, lines.Select(l => l.Text));
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = LogList.SelectedItems.Count > 0
                ? $"{LogList.SelectedItems.Count} Zeile(n) kopiert."
                : $"{_shown.Count} Zeilen kopiert.";
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Log kopieren", ex);
            StatusText.Text = "Kopieren ging gerade nicht, versuch es gleich noch einmal.";
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_inst == null)
            return;
        Shell.OpenFolder(Directory.Exists(_inst.LogsDir) ? _inst.LogsDir : _inst.GameDir);
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => SwitchTo(_inst);

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _all.Clear();
        _shown = [];
        LogList.ItemsSource = _shown;
        FollowCheck.IsChecked = true;
        EmptyText.Text = "Geleert. Neue Zeilen erscheinen hier, sobald das Spiel sie schreibt.\n" +
                         "„Neu laden“ holt das ganze Log zurück.";
        EmptyText.Visibility = Visibility.Visible;
        UpdateStatus();
    }
}

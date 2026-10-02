using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AxoClient.UI.Dialogs;

public sealed record CapeResult(string Name, byte[] Png, bool Global);

public sealed class CapeEditorDialog
{
    private const double FaceUnit = 15;
    private const double MaxOffset = 1.5;

    private static readonly (string Name, Color Color)[] Palette =
    [
        ("Schwarz", Color.FromRgb(0x10, 0x10, 0x10)), ("Dunkelgrau", Color.FromRgb(0x3A, 0x3A, 0x3A)),
        ("Grau", Color.FromRgb(0x8A, 0x8A, 0x8A)), ("Weiß", Color.FromRgb(0xF2, 0xF2, 0xF2)),
        ("Rot", Color.FromRgb(0xEF, 0x44, 0x44)), ("Orange", Color.FromRgb(0xF5, 0x9E, 0x0B)),
        ("Gelb", Color.FromRgb(0xFA, 0xCC, 0x15)), ("Grün", Color.FromRgb(0x4A, 0xDE, 0x80)),
        ("Dunkelgrün", Color.FromRgb(0x16, 0x65, 0x34)), ("Türkis", Color.FromRgb(0x14, 0xB8, 0xA6)),
        ("Himmelblau", Color.FromRgb(0x38, 0xBD, 0xF8)), ("Blau", Color.FromRgb(0x25, 0x63, 0xEB)),
        ("Lila", Color.FromRgb(0x7C, 0x3A, 0xED)), ("Violett", Color.FromRgb(0xA8, 0x55, 0xF7)),
        ("Pink", Color.FromRgb(0xEC, 0x48, 0x99)), ("Axo-Rosa", Color.FromRgb(0xF9, 0xA8, 0xD4)),
        ("Braun", Color.FromRgb(0x92, 0x40, 0x0E))
    ];

    private static readonly (string Label, int Factor)[] Resolutions =
        [("10 × 16 · Standard", 1), ("20 × 32 · HD", 2), ("40 × 64 · Ultra HD", 4)];

    private readonly AppServices _app;
    private readonly byte[] _skin;
    private readonly bool _slim;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly PixelCanvas _canvas = new();
    private readonly SkinViewer _viewer = new() { Width = 220, Height = 330 };
    private readonly TextBox _name = Ui.Input();
    private readonly TextBlock _footerText = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _currentSwatch = new() { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _hex = new();
    private readonly List<(RadioButton Button, string Tool)> _tools = [];
    private readonly ContentControl _left = new() { Focusable = false };
    private readonly CheckBox _global = new() { Content = "Für alle AxoClient-Spieler", FontSize = 12.5, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Button _save;

    private Color _color = Color.FromRgb(0xEC, 0x48, 0x99);
    private string _tool = "pen";
    private int _factor = 1;
    private bool _pixelMode = true;
    private bool _showElytra;
    private byte[]? _file;
    private CapeDesign? _fileDesign;
    private bool _fileAsIs;
    private CapeResult? _result;

    private FrameworkElement? _pixelView;
    private FrameworkElement? _fileView;
    private readonly Border _face = new() { Width = 10 * FaceUnit, Height = 16 * FaceUnit, Cursor = Cursors.SizeAll, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Image _faceImage = new() { Stretch = Stretch.None };
    private readonly Slider _zoom = new() { Minimum = CapeDesign.MinZoom, Maximum = CapeDesign.MaxZoom, Value = 1 };
    private readonly WrapPanel _fileSwatches = new();
    private readonly StackPanel _placement = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _fileTitle = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _fileSub = new() { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private Point? _dragFrom;

    private CapeEditorDialog(AppServices app)
    {
        _app = app;
        _skin = app.Accounts.Profile?.SkinPng ?? CapeDesign.PlainSkin();
        _slim = app.Accounts.Profile?.SkinSlim ?? false;
        _name.MaxLength = 24;
        Field.SetHint(_name, "z. B. Mein Axo-Cape");
        _name.Margin = new Thickness(0);
        _name.TextChanged += (_, _) => Validate();
        _footerText.Foreground = Ui.Resource<Brush>("MutedText");
        _global.Foreground = Ui.Resource<Brush>("TextSecondary");
        _global.Visibility = app.Capes.Status.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        _save = DialogParts.Primary("Cape speichern", Save, "Check");
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _canvas.CellPainted += Paint;
        _canvas.StrokeEnded += SchedulePreview;
        _canvas.Load(10, 16, Enumerable.Repeat<Color?>(Color.FromRgb(0x3B, 0x2A, 0x4E), 160).ToArray());
    }

    public static async Task<CapeResult?> ShowAsync(AppServices app)
    {
        var dialog = new CapeEditorDialog(app);
        try
        {
            return await dialog.RunAsync();
        }
        finally
        {
            dialog._previewTimer.Stop();
        }
    }

    private async Task<CapeResult?> RunAsync()
    {
        var group = Guid.NewGuid().ToString("N");
        var pixel = new RadioButton { Content = "Selbst pixeln", GroupName = group, IsChecked = true, Style = Ui.Resource<Style>("SegmentButton"), FontSize = 12.5 };
        var file = new RadioButton { Content = "Datei hochladen", GroupName = group, Style = Ui.Resource<Style>("SegmentButton"), FontSize = 12.5, Margin = new Thickness(2, 0, 0, 0) };
        pixel.Checked += (_, _) => SetMode(true);
        file.Checked += (_, _) => SetMode(false);
        var modes = new Border
        {
            Style = Ui.Resource<Style>("SegmentGroup"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 6, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { pixel, file } }
        };
        var header = DialogParts.Header("Eigenes Cape",
            "Pixel dein Cape selbst oder lade eine fertige Datei hoch. Nur für AxoClient-Spieler sichtbar.",
            DialogParts.IconTile("Brush"), _app.Dialogs.ClosePanel);
        var close = header.Children.OfType<Button>().First();
        DockPanel.SetDock(modes, Dock.Right);
        header.Children.Insert(header.Children.IndexOf(close) + 1, modes);
        header.Margin = new Thickness(22, 22, 22, 0);

        var leftHost = new Border { CornerRadius = new CornerRadius(12), Background = Ui.Frozen(Color.FromRgb(0x24, 0x24, 0x24)), ClipToBounds = true, Child = _left };
        var right = BuildPreviewColumn();
        var body = new Grid { Margin = new Thickness(22, 16, 22, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        Grid.SetColumn(right, 2);
        body.Children.Add(leftHost);
        body.Children.Add(right);

        var footer = DialogParts.Footer(_footerText, _global, DialogParts.Secondary("Abbrechen", _app.Dialogs.ClosePanel), _save);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);
        root.Height = Math.Min(680, (Application.Current.MainWindow?.ActualHeight ?? 800) - 60);

        SetMode(true);
        UpdatePreview();
        _viewer.TurnTo(SkinViewer.BackYaw, 0);
        await _app.Dialogs.ShowPanelAsync(root, 1060);
        return _result;
    }

    private FrameworkElement BuildPreviewColumn()
    {
        var group = Guid.NewGuid().ToString("N");
        var cape = new RadioButton { Content = "Cape", GroupName = group, IsChecked = true, Style = Ui.Resource<Style>("SegmentButton"), Height = 26, FontSize = 12, Padding = new Thickness(10, 0, 10, 0) };
        var elytra = new RadioButton { Content = "Elytra", GroupName = group, Style = Ui.Resource<Style>("SegmentButton"), Height = 26, FontSize = 12, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(2, 0, 0, 0) };
        cape.Checked += (_, _) => SetElytra(false);
        elytra.Checked += (_, _) => SetElytra(true);
        var toggle = new Border
        {
            Style = Ui.Resource<Style>("SegmentGroup"),
            Background = Ui.Resource<Brush>("ContentBg"),
            CornerRadius = new CornerRadius(9),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { cape, elytra } }
        };
        var top = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        top.Children.Add(toggle);
        top.Children.Add(new TextBlock { Text = "VORSCHAU", Style = Ui.Resource<Style>("OverLabel"), VerticalAlignment = VerticalAlignment.Center });
        var stage = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top);
        stage.Children.Add(top);
        stage.Children.Add(_viewer);
        var stageBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.85),
                GradientOrigin = new Point(0.5, 0.85),
                RadiusX = 1.2,
                RadiusY = 0.7,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x33, 0x33, 0x33), 0),
                    new GradientStop(Color.FromRgb(0x2A, 0x2A, 0x2A), 0.55),
                    new GradientStop(Color.FromRgb(0x25, 0x25, 0x25), 1)
                }
            },
            Child = stage
        };
        var nameBlock = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Children = { Ui.Label("Name"), _name } };
        var column = new DockPanel();
        DockPanel.SetDock(nameBlock, Dock.Bottom);
        column.Children.Add(nameBlock);
        column.Children.Add(stageBorder);
        return column;
    }

    private void SetElytra(bool elytra)
    {
        _showElytra = elytra;
        UpdatePreview();
        _viewer.TurnTo(SkinViewer.BackYaw);
    }

    private void SetMode(bool pixel)
    {
        _pixelMode = pixel;
        _left.Content = pixel ? _pixelView ??= BuildPixelView() : _fileView ??= BuildFileView();
        Validate();
        UpdatePreview();
    }

    private FrameworkElement BuildPixelView()
    {
        var canvasArea = new Grid { Background = DotGrid() };
        var tools = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
        var group = Guid.NewGuid().ToString("N");
        foreach (var (tool, icon, label) in new[] { ("pen", "Pencil", "Stift"), ("eraser", "Eraser", "Radierer"), ("fill", "Bucket", "Füllen"), ("pick", "Pipette", "Farbe aufnehmen") })
        {
            var button = new RadioButton
            {
                GroupName = group,
                Style = Ui.Resource<Style>("SegmentButton"),
                Height = 36,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                IsChecked = tool == _tool,
                ToolTip = label,
                Content = new Icon { Kind = icon, Size = 16 }
            };
            button.Checked += (_, _) => _tool = tool;
            _tools.Add((button, tool));
            tools.Children.Add(button);
        }

        var palette = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (name, color) in Palette)
        {
            var swatch = new Button
            {
                Style = Ui.Resource<Style>("ButtonBase"),
                Background = new SolidColorBrush(color),
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = name,
                Template = (ControlTemplate)Application.Current.FindResource("PlainButtonTemplate")
            };
            Field.SetCorner(swatch, new CornerRadius(6));
            swatch.Click += (_, _) => SetColor(color);
            palette.Children.Add(swatch);
        }
        var custom = new Button
        {
            Style = Ui.Resource<Style>("ButtonBase"),
            Height = 24,
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = "Eigene Farbe",
            Template = (ControlTemplate)Application.Current.FindResource("PlainButtonTemplate"),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xEF, 0x44, 0x44), 0), new(Color.FromRgb(0xF5, 0x9E, 0x0B), 0.2), new(Color.FromRgb(0x4A, 0xDE, 0x80), 0.4),
                new(Color.FromRgb(0x38, 0xBD, 0xF8), 0.6), new(Color.FromRgb(0x7C, 0x3A, 0xED), 0.8), new(Color.FromRgb(0xEC, 0x48, 0x99), 1)
            }, 45)
        };
        Field.SetCorner(custom, new CornerRadius(6));
        custom.Click += (_, _) => PickCustomColor();
        palette.Children.Add(custom);

        _hex.Style = Ui.Resource<Style>("LauncherTextBox");
        _hex.Height = 28;
        _hex.FontFamily = Ui.Resource<FontFamily>("MonoFont");
        _hex.FontSize = 12;
        _hex.Padding = new Thickness(8, 0, 8, 0);
        _hex.Width = 92;
        Field.SetCorner(_hex, new CornerRadius(6));
        _hex.LostFocus += (_, _) => ApplyHex();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            ApplyHex();
            e.Handled = true;
        };
        var current = new StackPanel { Orientation = Orientation.Horizontal, Children = { _currentSwatch, _hex } };

        var resolution = Ui.Combo(Resolutions.Select(r => (object)r.Label));
        resolution.SelectedIndex = 0;
        resolution.Height = 36;
        resolution.FontSize = 12.5;
        resolution.Margin = new Thickness(0, 0, 0, 6);
        resolution.SelectionChanged += (_, _) =>
        {
            _factor = Resolutions[Math.Max(0, resolution.SelectedIndex)].Factor;
            _canvas.Resize(10 * _factor, 16 * _factor);
            FitCanvas();
            SchedulePreview();
        };

        var import = DialogParts.Make("SecondButton", "Bild importieren", ImportImage, "Image");
        import.Height = 36;
        import.FontSize = 12.5;
        import.Background = Ui.Resource<Brush>("ChipBg");
        var clear = DialogParts.Make("TextButton", "Alles leeren", () =>
        {
            _canvas.Clear();
            SchedulePreview();
        });
        clear.Height = 32;
        clear.FontSize = 12.5;
        clear.Margin = new Thickness(0, 6, 0, 0);

        var toolsColumn = new DockPanel();
        var bottom = new StackPanel { Children = { import, clear } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        toolsColumn.Children.Add(bottom);
        toolsColumn.Children.Add(new StackPanel
        {
            Children =
            {
                Ui.Label("Werkzeug"), tools,
                Ui.Label("Farbe"), palette, current,
                Spaced(Ui.Label("Pixel"), 16), resolution,
                new TextBlock { Text = "Beim Wechsel wird dein Bild auf die neue Größe umgerechnet.", FontSize = 11.5, Foreground = Ui.Resource<Brush>("LabelText"), TextWrapping = TextWrapping.Wrap }
            }
        });
        var toolsBorder = new Border
        {
            Width = 210,
            Padding = new Thickness(16),
            BorderBrush = Ui.Resource<Brush>("DividerSoft"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = toolsColumn
        };

        var hint = new TextBlock { Text = "Klicken und ziehen zum Malen", FontSize = 11.5, Foreground = Ui.Resource<Brush>("DimText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        canvasArea.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Effect = Ui.Resource<System.Windows.Media.Effects.Effect>("SoftShadow"), Child = _canvas }, hint }
        });
        canvasArea.SizeChanged += (_, _) => FitCanvas();

        var view = new DockPanel();
        DockPanel.SetDock(toolsBorder, Dock.Left);
        view.Children.Add(toolsBorder);
        view.Children.Add(canvasArea);
        SetColor(_color);
        return view;

        void FitCanvas()
        {
            var width = Math.Max(120, canvasArea.ActualWidth - 60);
            var height = Math.Max(160, canvasArea.ActualHeight - 70);
            _canvas.CellSize = Math.Floor(Math.Min(width / _canvas.Columns, height / _canvas.Rows));
            _canvas.InvalidateMeasure();
            _canvas.InvalidateVisual();
        }
    }

    private static FrameworkElement Spaced(FrameworkElement element, double top)
    {
        element.Margin = new Thickness(0, top, 0, element.Margin.Bottom);
        return element;
    }

    private static Brush DotGrid()
    {
        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 14, 14),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(Ui.Frozen(Color.FromRgb(0x2C, 0x2C, 0x2C)), null, new EllipseGeometry(new Point(1, 1), 1, 1))
        };
        brush.Freeze();
        return brush;
    }

    private FrameworkElement BuildFileView()
    {
        var drop = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = Ui.Resource<Brush>("FieldBorder"),
            BorderThickness = new Thickness(1.5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            AllowDrop = true,
            MinHeight = 200,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Border
                    {
                        Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Background = Ui.Resource<Brush>("AccentSoft"), Margin = new Thickness(0, 0, 0, 12),
                        Child = new Icon { Kind = "Upload", Size = 22, Foreground = Ui.Resource<Brush>("AccentText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    },
                    _fileTitle, _fileSub
                }
            }
        };
        _fileSub.Foreground = Ui.Resource<Brush>("MutedText");
        drop.MouseLeftButtonUp += (_, _) => ChooseFile();
        drop.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        drop.Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                LoadFile(files[0]);
        };

        var info = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, -10, 0) };
        foreach (var (title, text) in new[] { ("Standard", "64 × 32 Pixel"), ("HD", "128 × 64 oder 256 × 128"), ("Elytra", "wird aus derselben Datei erzeugt") })
            info.Children.Add(new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(10),
                Background = Ui.Resource<Brush>("CardBg"),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Resource<Brush>("SubtleText") },
                        new TextBlock { Text = text, FontSize = 12, Foreground = Ui.Resource<Brush>("MutedText"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap }
                    }
                }
            });

        _face.Child = _faceImage;
        _face.MouseLeftButtonDown += (_, e) =>
        {
            _dragFrom = e.GetPosition(_face);
            _face.CaptureMouse();
        };
        _face.MouseMove += (_, e) =>
        {
            if (_fileDesign == null || _dragFrom is not { } from || !_face.IsMouseCaptured)
                return;
            var to = e.GetPosition(_face);
            _fileDesign.OffsetX = Math.Clamp(_fileDesign.OffsetX + (to.X - from.X) / _face.Width, -MaxOffset, MaxOffset);
            _fileDesign.OffsetY = Math.Clamp(_fileDesign.OffsetY + (to.Y - from.Y) / _face.Height, -MaxOffset, MaxOffset);
            _dragFrom = to;
            DesignChanged();
        };
        _face.MouseLeftButtonUp += (_, _) =>
        {
            _dragFrom = null;
            _face.ReleaseMouseCapture();
        };
        _face.MouseWheel += (_, e) =>
        {
            _zoom.Value = Math.Clamp(_zoom.Value * Math.Pow(1.1, e.Delta / 120.0), CapeDesign.MinZoom, CapeDesign.MaxZoom);
            e.Handled = true;
        };
        _zoom.ValueChanged += (_, _) =>
        {
            if (_fileDesign == null)
                return;
            _fileDesign.Zoom = _zoom.Value;
            DesignChanged();
        };
        var faceFrame = new Border
        {
            Child = _face,
            BorderBrush = Ui.Resource<Brush>("FieldBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 18, 0)
        };
        var controls = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Das Bild ist keine fertige Cape-Textur. Schiebe es zurecht, Mausrad zoomt.", FontSize = 12.5, Foreground = Ui.Resource<Brush>("TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
                Ui.Label("Größe"), _zoom,
                Spaced(Ui.Label("Hintergrund"), 12), _fileSwatches
            }
        };
        var placementRow = new DockPanel();
        DockPanel.SetDock(faceFrame, Dock.Left);
        placementRow.Children.Add(faceFrame);
        placementRow.Children.Add(controls);
        _placement.Children.Add(placementRow);
        _placement.Margin = new Thickness(0, 16, 0, 0);

        _fileTitle.Text = "PNG auswählen oder hierher ziehen";
        _fileSub.Text = "Fertige Cape-Texturen werden unverändert hochgeladen";
        _fileTitle.Foreground = Ui.Resource<Brush>("TextStrong");
        var layout = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(info, Dock.Bottom);
        DockPanel.SetDock(_placement, Dock.Bottom);
        layout.Children.Add(info);
        layout.Children.Add(_placement);
        layout.Children.Add(drop);
        return layout;
    }

    private void ChooseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Title = "Bild für das Cape auswählen"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) == true)
            LoadFile(dialog.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            _fileDesign = CapeDesign.FromPng(bytes);
            _file = bytes;
            _fileAsIs = ClientCapeService.UploadProblem(bytes) == null;
            var image = _fileDesign.Image;
            _fileTitle.Text = Path.GetFileName(path);
            _fileSub.Text = $"{image.PixelWidth} × {image.PixelHeight} Pixel · " +
                            (_fileAsIs ? "fertige Cape-Textur, wird unverändert hochgeladen" : "wird auf das Cape gelegt") + " · Andere Datei wählen";
            _fileSub.Foreground = Ui.Resource<Brush>("AccentText");
            if (_name.Text.Trim().Length == 0)
                _name.Text = Path.GetFileNameWithoutExtension(path);
            Ui.Show(_placement, !_fileAsIs);
            _fileSwatches.Children.Clear();
            foreach (var (name, color) in Palette.Take(10).Prepend(("Aus dem Bild", _fileDesign.ImageColor)))
            {
                var swatch = new Button
                {
                    Style = Ui.Resource<Style>("ButtonBase"),
                    Background = new SolidColorBrush(color),
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 6),
                    ToolTip = name,
                    Template = (ControlTemplate)Application.Current.FindResource("PlainButtonTemplate")
                };
                Field.SetCorner(swatch, new CornerRadius(6));
                swatch.Click += (_, _) =>
                {
                    _fileDesign.Background = color;
                    DesignChanged();
                };
                _fileSwatches.Children.Add(swatch);
            }
            _zoom.Value = 1;
            DesignChanged();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Bild für das Cape laden", ex);
            _fileDesign = null;
            _file = null;
            _fileTitle.Text = "Die Datei ist kein lesbares Bild";
            _fileSub.Text = "Wähle eine andere Datei";
        }
        Validate();
    }

    private void DesignChanged()
    {
        if (_fileDesign == null)
            return;
        _faceImage.Source = _fileDesign.RenderOuterFace(FaceUnit);
        SchedulePreview();
    }

    private void SetColor(Color color)
    {
        _color = color;
        _currentSwatch.Background = new SolidColorBrush(color);
        _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (_tool is "eraser" or "pick")
            SelectTool("pen");
    }

    private void SelectTool(string tool)
    {
        _tool = tool;
        foreach (var (button, name) in _tools)
            button.IsChecked = name == tool;
    }

    private void ApplyHex()
    {
        var hex = _hex.Text.Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            SetColor(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        else
            _hex.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
    }

    private void PickCustomColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B)
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    private void Paint(int index, bool start)
    {
        switch (_tool)
        {
            case "pen":
                _canvas[index] = _color;
                break;
            case "eraser":
                _canvas[index] = null;
                break;
            case "fill" when start:
                _canvas.Fill(index, _color);
                break;
            case "pick" when start:
                if (_canvas[index] is { } picked)
                    SetColor(picked);
                break;
        }
        Validate();
    }

    private void ImportImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Title = "Bild in die Leinwand übernehmen"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;
        try
        {
            var image = Images.Decode(File.ReadAllBytes(dialog.FileName));
            int columns = _canvas.Columns, rows = _canvas.Rows;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                var scale = Math.Max((double)columns / image.PixelWidth, (double)rows / image.PixelHeight);
                double width = image.PixelWidth * scale, height = image.PixelHeight * scale;
                dc.DrawImage(image, new Rect((columns - width) / 2, (rows - height) / 2, width, height));
            }
            var bitmap = new RenderTargetBitmap(columns, rows, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var (pixels, _, _) = Images.ReadBgra(bitmap);
            var colors = new Color?[columns * rows];
            for (var i = 0; i < colors.Length; i++)
                colors[i] = pixels[i * 4 + 3] < 40 ? null : Color.FromRgb(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]);
            _canvas.Load(columns, rows, colors);
            if (_name.Text.Trim().Length == 0)
                _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            SchedulePreview();
            Validate();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Bild in die Cape-Leinwand übernehmen", ex);
            _ = _app.Dialogs.ShowMessageAsync("Kein Bild", "Diese Datei kann nicht als Bild geöffnet werden.");
        }
    }

    private CapeDesign? PixelDesign()
    {
        if (_canvas.IsEmpty)
            return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_canvas.ToBitmap()));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var design = CapeDesign.FromPng(stream.ToArray());
        design.ImageInside = true;
        design.ImageOnElytra = true;
        return design;
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdatePreview()
    {
        _previewTimer.Stop();
        byte[]? cape = null;
        try
        {
            cape = _pixelMode
                ? PixelDesign()?.ToPng(CapeDesign.PreviewScale)
                : _fileAsIs ? _file : _fileDesign?.ToPng(CapeDesign.PreviewScale);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Cape-Vorschau", ex);
        }
        _viewer.SetSkin(_skin, _slim, cape, _showElytra);
    }

    private bool Validate()
    {
        var problem = _name.Text.Trim().Length == 0 ? "Gib dem Cape einen Namen."
            : _pixelMode ? _canvas.IsEmpty ? "Male etwas auf die Leinwand." : null
            : _fileDesign == null ? "Wähle eine Datei aus." : null;
        _footerText.Text = problem ?? (_pixelMode
            ? $"{_canvas.Columns} × {_canvas.Rows} Pixel · Rückseite und Elytra werden automatisch erzeugt"
            : _fileAsIs ? "Die Datei wird unverändert hochgeladen" : "Das Bild wird auf Vorder- und Innenseite gelegt");
        _footerText.Foreground = Ui.Resource<Brush>(problem != null ? "Warn" : "MutedText");
        _save.IsEnabled = problem == null;
        return problem == null;
    }

    private void Save()
    {
        if (!Validate())
            return;
        byte[] png;
        try
        {
            png = _pixelMode
                ? PixelDesign()!.ToUploadPng()
                : _fileAsIs ? _file! : _fileDesign!.ToUploadPng();
        }
        catch (Exception ex)
        {
            _ = _app.Dialogs.ShowErrorAsync("Cape konnte nicht erzeugt werden", ex);
            return;
        }
        _result = new CapeResult(_name.Text.Trim(), png, _global.IsChecked == true);
        _app.Dialogs.ClosePanel();
    }
}

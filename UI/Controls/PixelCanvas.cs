using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxoClient.UI.Controls;

public sealed class PixelCanvas : FrameworkElement
{
    private static readonly Brush CheckerA = Ui.Frozen(Color.FromRgb(0x2E, 0x2E, 0x2E));
    private static readonly Brush CheckerB = Ui.Frozen(Color.FromRgb(0x26, 0x26, 0x26));
    private static readonly Pen GridPen = MakePen();

    private Color?[] _pixels = new Color?[10 * 16];
    private int _hover = -1;
    private bool _painting;

    public PixelCanvas()
    {
        Cursor = Cursors.Cross;
        Focusable = false;
    }

    public int Columns { get; private set; } = 10;
    public int Rows { get; private set; } = 16;
    public double CellSize { get; set; } = 28;

    public event Action<int, bool>? CellPainted;
    public event Action? StrokeEnded;

    private static Pen MakePen()
    {
        var pen = new Pen(Ui.Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), 1);
        pen.Freeze();
        return pen;
    }

    public Color? this[int index]
    {
        get => _pixels[index];
        set
        {
            _pixels[index] = value;
            InvalidateVisual();
        }
    }

    public Color?[] Pixels => _pixels;

    public void Load(int columns, int rows, Color?[] pixels)
    {
        Columns = columns;
        Rows = rows;
        _pixels = pixels;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Resize(int columns, int rows)
    {
        var next = new Color?[columns * rows];
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
            next[y * columns + x] = _pixels[y * Rows / rows * Columns + x * Columns / columns];
        Load(columns, rows, next);
    }

    public void Clear() => Load(Columns, Rows, new Color?[Columns * Rows]);

    public void Fill(int start, Color? color)
    {
        var target = _pixels[start];
        if (target == color)
            return;
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            if (i < 0 || i >= _pixels.Length || _pixels[i] != target)
                continue;
            _pixels[i] = color;
            var (x, y) = (i % Columns, i / Columns);
            if (x > 0)
                stack.Push(i - 1);
            if (x < Columns - 1)
                stack.Push(i + 1);
            if (y > 0)
                stack.Push(i - Columns);
            if (y < Rows - 1)
                stack.Push(i + Columns);
        }
        InvalidateVisual();
    }

    public BitmapSource ToBitmap()
    {
        var pixels = new byte[Columns * Rows * 4];
        for (var i = 0; i < _pixels.Length; i++)
        {
            if (_pixels[i] is not { } c)
                continue;
            pixels[i * 4] = c.B;
            pixels[i * 4 + 1] = c.G;
            pixels[i * 4 + 2] = c.R;
            pixels[i * 4 + 3] = 255;
        }
        var bitmap = BitmapSource.Create(Columns, Rows, 96, 96, PixelFormats.Bgra32, null, pixels, Columns * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public bool IsEmpty => _pixels.All(p => p == null);

    protected override Size MeasureOverride(Size availableSize) => new(Columns * CellSize, Rows * CellSize);

    protected override void OnRender(DrawingContext dc)
    {
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
        {
            var rect = new Rect(x * CellSize, y * CellSize, CellSize, CellSize);
            if (_pixels[y * Columns + x] is { } color)
            {
                dc.DrawRectangle(new SolidColorBrush(color), null, rect);
            }
            else
            {
                var half = CellSize / 2;
                dc.DrawRectangle(CheckerA, null, rect);
                dc.DrawRectangle(CheckerB, null, new Rect(rect.X, rect.Y, half, half));
                dc.DrawRectangle(CheckerB, null, new Rect(rect.X + half, rect.Y + half, half, half));
            }
        }
        if (CellSize >= 10)
        {
            for (var x = 1; x < Columns; x++)
                dc.DrawLine(GridPen, new Point(x * CellSize, 0), new Point(x * CellSize, Rows * CellSize));
            for (var y = 1; y < Rows; y++)
                dc.DrawLine(GridPen, new Point(0, y * CellSize), new Point(Columns * CellSize, y * CellSize));
        }
        if (_hover >= 0)
            dc.DrawRectangle(null, new Pen(Ui.Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), 1.2),
                new Rect(_hover % Columns * CellSize + 0.6, _hover / Columns * CellSize + 0.6, CellSize - 1.2, CellSize - 1.2));
    }

    private int IndexAt(Point point)
    {
        var x = (int)(point.X / CellSize);
        var y = (int)(point.Y / CellSize);
        return x < 0 || y < 0 || x >= Columns || y >= Rows ? -1 : y * Columns + x;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var index = IndexAt(e.GetPosition(this));
        if (index < 0)
            return;
        _painting = true;
        CaptureMouse();
        CellPainted?.Invoke(index, true);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.GetPosition(this));
        if (index != _hover)
        {
            _hover = index;
            InvalidateVisual();
        }
        if (_painting && index >= 0)
            CellPainted?.Invoke(index, false);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_painting)
            return;
        _painting = false;
        ReleaseMouseCapture();
        StrokeEnded?.Invoke();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }
}

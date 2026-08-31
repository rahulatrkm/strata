using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Strata.Core;

namespace Strata.App;

/// <summary>The treemap. Blocks are coloured by the kind of file filling them.</summary>
public sealed class TreemapView : FrameworkElement
{
    private IReadOnlyList<TreeItem> _items = [];
    private List<Tile<TreeItem>> _tiles = [];
    private TreeItem? _hot;

    private static readonly Dictionary<string, Brush> Brushes = BuildBrushes();
    private static readonly Brush Edge = new SolidColorBrush(Color.FromArgb(220, 17, 20, 26));
    private static readonly Brush Label = System.Windows.Media.Brushes.White;
    private static readonly Typeface Face = new("Segoe UI");

    public bool Binary { get; set; }
    public event Action<TreeItem>? Activated;
    public event Action<TreeItem?>? Hovered;

    public TreemapView()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        Edge.Freeze();
    }

    private static Dictionary<string, Brush> BuildBrushes()
    {
        var map = new Dictionary<string, Brush>();
        foreach (var (name, hex) in Categories.Colours)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            map[name] = brush;
        }
        return map;
    }

    public void SetItems(IReadOnlyList<TreeItem> items)
    {
        _items = items;
        _hot = null;
        Relayout();
        InvalidateVisual();
    }

    private void Relayout()
    {
        _tiles = ActualWidth > 0 && ActualHeight > 0
            ? Treemap.Squarify(_items, i => i.Size, 0, 0, ActualWidth, ActualHeight)
            : [];
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Relayout();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 26, 31)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (_tiles.Count == 0)
        {
            Draw(dc, "Nothing to show here.", 12, 12, 13, System.Windows.Media.Brushes.Gray);
            return;
        }

        foreach (var tile in _tiles)
        {
            var brush = Brushes.TryGetValue(tile.Item.Category, out var b) ? b : System.Windows.Media.Brushes.Gray;
            var rect = new Rect(tile.X, tile.Y, Math.Max(0, tile.W - 1), Math.Max(0, tile.H - 1));

            dc.PushOpacity(tile.Item.IsDirectory ? 0.92 : 0.6);
            dc.DrawRectangle(brush, null, rect);
            dc.Pop();

            var pen = new Pen(ReferenceEquals(tile.Item, _hot) ? System.Windows.Media.Brushes.White : Edge,
                ReferenceEquals(tile.Item, _hot) ? 2 : 1);
            pen.Freeze();
            dc.DrawRectangle(null, pen, rect);

            if (tile.W > 62 && tile.H > 24)
            {
                dc.PushClip(new RectangleGeometry(rect));
                Draw(dc, tile.Item.Name + (tile.Item.IsDirectory ? "\\" : ""), tile.X + 6, tile.Y + 5, 12.5, Label);
                if (tile.H > 42)
                    Draw(dc, ByteSize.Format(tile.Item.Size, Binary), tile.X + 6, tile.Y + 23, 11.5,
                        new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));
                dc.Pop();
            }
        }
    }

    private static void Draw(DrawingContext dc, string text, double x, double y, double size, Brush brush) =>
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Face, size, brush, VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).PixelsPerDip),
            new Point(x, y));

    private TreeItem? HitTest(Point p)
    {
        foreach (var t in _tiles)
            if (p.X >= t.X && p.X < t.X + t.W && p.Y >= t.Y && p.Y < t.Y + t.H) return t.Item;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var item = HitTest(e.GetPosition(this));
        if (!ReferenceEquals(item, _hot))
        {
            _hot = item;
            Hovered?.Invoke(item);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hot = null;
        Hovered?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var item = HitTest(e.GetPosition(this));
        if (item is not null) Activated?.Invoke(item);
    }
}

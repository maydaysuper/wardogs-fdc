using System.Drawing;
using System.Drawing.Drawing2D;

namespace WardogsNavigator.UI;

public sealed class MapCanvas : Control
{
    private Bitmap? _map;
    private RoutePlan? _route;
    private MapPoint? _current;
    private MapPoint? _target;
    private MapDefinition? _definition;

    public event Action<MapPoint>? MapClicked;

    public MapCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 20, 24);
        Cursor = Cursors.Cross;
    }

    public void SetMap(Bitmap? map, MapDefinition? definition)
    {
        _map = map;
        _definition = definition;
        Invalidate();
    }

    public void SetState(MapPoint? current, MapPoint? target, RoutePlan? route)
    {
        _current = current;
        _target = target;
        _route = route;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var rect = MapRect();
        if (!rect.Contains(e.Location)) return;

        var nx = (e.X - rect.Left) / (double)rect.Width;
        var ny = (e.Y - rect.Top) / (double)rect.Height;
        var p = new MapPoint(
            nx * MapPoint.MapSize,
            MapPoint.MapSize - ny * MapPoint.MapSize);
        MapClicked?.Invoke(p);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = MapRect();

        if (_map != null)
        {
            e.Graphics.DrawImage(_map, rect);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(30, 34, 40));
            e.Graphics.FillRectangle(brush, rect);
            TextRenderer.DrawText(
                e.Graphics,
                "正在加载真实地图…",
                Font,
                rect,
                Color.LightGray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        DrawMarkers(e.Graphics, rect);
        DrawRoute(e.Graphics, rect);

        if (_current is MapPoint current)
            DrawDot(e.Graphics, rect, current, Color.DeepSkyBlue, 7, "YOU");
        if (_target is MapPoint target)
            DrawDot(e.Graphics, rect, target, Color.OrangeRed, 7, "TARGET");
    }

    private void DrawRoute(Graphics g, Rectangle rect)
    {
        if (_route?.Points == null || _route.Points.Count < 2) return;

        using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 7)
        {
            LineJoin = LineJoin.Round
        };
        using var pen = new Pen(Color.LimeGreen, 3.2f)
        {
            LineJoin = LineJoin.Round
        };

        var pts = _route.Points.Select(p => ToPixel(rect, p)).ToArray();
        g.DrawLines(shadow, pts);
        g.DrawLines(pen, pts);
    }

    private void DrawMarkers(Graphics g, Rectangle rect)
    {
        if (_definition == null) return;

        foreach (var marker in _definition.Markers)
        {
            var p = ToPixel(rect, marker.Position);
            var color = marker.Kind.Equals("tower", StringComparison.OrdinalIgnoreCase)
                ? Color.Gold
                : Color.FromArgb(210, 230, 230, 230);

            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, p.X - 4, p.Y - 4, 8, 8);

            var size = TextRenderer.MeasureText(marker.Label, Font);
            using var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            g.FillRectangle(bg, p.X + 6, p.Y - size.Height / 2, size.Width, size.Height);
            TextRenderer.DrawText(g, marker.Label, Font, new Point(p.X + 6, p.Y - size.Height / 2), color);
        }
    }

    private void DrawDot(Graphics g, Rectangle rect, MapPoint p, Color color, int radius, string label)
    {
        var q = ToPixel(rect, p);
        using var outline = new Pen(Color.Black, 3);
        using var brush = new SolidBrush(color);

        g.FillEllipse(brush, q.X - radius, q.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(outline, q.X - radius, q.Y - radius, radius * 2, radius * 2);
        TextRenderer.DrawText(g, label, Font, new Point(q.X + radius + 4, q.Y - 9), color);
    }

    private static Point ToPixel(Rectangle rect, MapPoint p) => new(
        rect.Left + (int)Math.Round(p.X / MapPoint.MapSize * rect.Width),
        rect.Top + (int)Math.Round((MapPoint.MapSize - p.Y) / MapPoint.MapSize * rect.Height));

    private Rectangle MapRect()
    {
        var size = Math.Max(1, Math.Min(ClientSize.Width - 20, ClientSize.Height - 20));
        return new Rectangle(
            (ClientSize.Width - size) / 2,
            (ClientSize.Height - size) / 2,
            size,
            size);
    }
}

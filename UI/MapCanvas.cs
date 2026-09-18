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
    private RoadGraph? _roadGraph;
    private IReadOnlyList<NavigationHazard> _hazards = Array.Empty<NavigationHazard>();
    private IReadOnlyList<VisionEdgeEvidence> _visionEvidence =
        Array.Empty<VisionEdgeEvidence>();

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

    public void SetRoadGraph(RoadGraph? graph)
    {
        _roadGraph = graph;
        Invalidate();
    }

    public void SetHazards(IReadOnlyList<NavigationHazard>? hazards)
    {
        _hazards = hazards ?? Array.Empty<NavigationHazard>();
        Invalidate();
    }

    public void SetVisionEvidence(
        IReadOnlyList<VisionEdgeEvidence>? evidence)
    {
        _visionEvidence =
            evidence ??
            Array.Empty<VisionEdgeEvidence>();
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

        DrawHazards(e.Graphics, rect);
        DrawRoadGraph(e.Graphics, rect);
        DrawVisionEvidence(e.Graphics, rect);
        DrawMarkers(e.Graphics, rect);
        DrawRoute(e.Graphics, rect);

        if (_current is MapPoint current)
            DrawDot(e.Graphics, rect, current, Color.DeepSkyBlue, 7, "YOU");
        if (_target is MapPoint target)
            DrawDot(e.Graphics, rect, target, Color.OrangeRed, 7, "TARGET");
    }



    private void DrawHazards(Graphics g, Rectangle rect)
    {
        if (_hazards.Count == 0) return;

        foreach (var hazard in _hazards.Where(h => h.ExpiresUtc > DateTime.UtcNow))
        {
            var center = ToPixel(rect, hazard.Center);
            var radiusUnits = hazard.RadiusMeters / MapPoint.MetersPerUnit;
            var radiusPx = (float)(radiusUnits / MapPoint.MapSize * rect.Width);

            var severity = Math.Clamp(hazard.Severity, 0, 1);
            var fillAlpha = 22 + (int)(severity * 35);
            var strokeAlpha = 90 + (int)(severity * 120);

            using var fill = new SolidBrush(Color.FromArgb(fillAlpha, 255, 70, 70));
            using var pen = new Pen(Color.FromArgb(strokeAlpha, 255, 95, 70), 2)
            {
                DashStyle = DashStyle.Dash
            };

            g.FillEllipse(
                fill,
                center.X - radiusPx,
                center.Y - radiusPx,
                radiusPx * 2,
                radiusPx * 2);

            g.DrawEllipse(
                pen,
                center.X - radiusPx,
                center.Y - radiusPx,
                radiusPx * 2,
                radiusPx * 2);

            TextRenderer.DrawText(
                g,
                hazard.Label,
                Font,
                new Point(center.X + (int)radiusPx + 4, center.Y - 8),
                Color.OrangeRed);
        }
    }


    private void DrawVisionEvidence(
        Graphics g,
        Rectangle rect)
    {
        if (_roadGraph == null ||
            _visionEvidence.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var active = _visionEvidence
            .Where(x => x.ExpiresUtc > now)
            .GroupBy(
                x => x.EdgeId,
                StringComparer.OrdinalIgnoreCase)
            .Select(gp => gp
                .OrderByDescending(x => x.Confidence)
                .First())
            .ToDictionary(
                x => x.EdgeId,
                StringComparer.OrdinalIgnoreCase);

        if (active.Count == 0)
            return;

        var nodes = _roadGraph.Nodes.ToDictionary(
            n => n.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in _roadGraph.Edges)
        {
            if (!active.TryGetValue(
                    edge.Id,
                    out var evidence) ||
                !nodes.TryGetValue(edge.A, out var a) ||
                !nodes.TryGetValue(edge.B, out var b))
                continue;

            var p1 = ToPixel(rect, a.Position);
            var p2 = ToPixel(rect, b.Position);

            var color = evidence.Kind switch
            {
                "blocked" => Color.Red,
                "danger" => Color.OrangeRed,
                _ => Color.Magenta
            };

            using var shadow = new Pen(
                Color.FromArgb(190, 0, 0, 0),
                9);

            using var pen = new Pen(
                Color.FromArgb(
                    120 +
                    (int)(Math.Clamp(
                        evidence.Confidence,
                        0,
                        1) * 120),
                    color),
                4)
            {
                DashStyle = DashStyle.Dash,
                LineJoin = LineJoin.Round
            };

            g.DrawLine(shadow, p1, p2);
            g.DrawLine(pen, p1, p2);
        }
    }

    private void DrawRoadGraph(Graphics g, Rectangle rect)
    {
        if (_roadGraph == null || _roadGraph.Nodes.Count == 0) return;

        var nodes = _roadGraph.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in _roadGraph.Edges)
        {
            if (!nodes.TryGetValue(edge.A, out var a) ||
                !nodes.TryGetValue(edge.B, out var b))
                continue;

            var p1 = ToPixel(rect, a.Position);
            var p2 = ToPixel(rect, b.Position);

            var isAuto = edge.Source.Equals("auto", StringComparison.OrdinalIgnoreCase);

            var color = edge.Blocked
                ? Color.FromArgb(210, 230, 70, 70)
                : edge.Class switch
                {
                    RoadClass.Primary => Color.FromArgb(220, 40, 215, 255),
                    RoadClass.Bridge => Color.FromArgb(230, 255, 180, 50),
                    RoadClass.Track => Color.FromArgb(180, 190, 190, 190),
                    _ => Color.FromArgb(200, 70, 145, 255)
                };

            var width = edge.Traversals > 0 ? 3.4f : isAuto ? 1.6f : 2.2f;

            if (isAuto)
            {
                var alpha = 65 + (int)(Math.Clamp(edge.AutoScore, 0, 1) * 85);
                color = Color.FromArgb(alpha, color);
            }
            else if (!edge.Verified)
            {
                color = Color.FromArgb(120, color);
            }

            using var pen = new Pen(color, width)
            {
                LineJoin = LineJoin.Round,
                DashStyle = edge.Blocked || isAuto ? DashStyle.Dash : DashStyle.Solid
            };

            g.DrawLine(pen, p1, p2);
        }

        foreach (var node in _roadGraph.Nodes)
        {
            var p = ToPixel(rect, node.Position);
            using var brush = new SolidBrush(Color.FromArgb(205, 180, 100, 255));
            g.FillEllipse(brush, p.X - 2.5f, p.Y - 2.5f, 5, 5);
        }
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

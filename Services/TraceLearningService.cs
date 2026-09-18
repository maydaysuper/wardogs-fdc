namespace WardogsNavigator.Services;

public sealed class TraceLearningService
{
    private readonly List<MapPoint> _raw = new();

    public bool IsRecording { get; private set; }
    public IReadOnlyList<MapPoint> RawPoints => _raw;

    public void Start(MapPoint? current = null)
    {
        _raw.Clear();
        IsRecording = true;
        if (current is MapPoint p) _raw.Add(p);
    }

    public void Cancel()
    {
        IsRecording = false;
        _raw.Clear();
    }

    public bool Accept(MapPoint point)
    {
        if (!IsRecording || !point.IsInsideMap) return false;

        if (_raw.Count == 0)
        {
            _raw.Add(point);
            return true;
        }

        var distance = _raw[^1].DistanceMeters(point);

        // Ignore OCR jitter; reject obvious OCR jumps/teleports.
        if (distance < 8) return false;
        if (distance > 260) return false;

        _raw.Add(point);
        return true;
    }

    public List<MapPoint> StopAndSimplify(double epsilonMeters = 14)
    {
        IsRecording = false;
        if (_raw.Count < 2)
        {
            _raw.Clear();
            return new List<MapPoint>();
        }

        var simplified = Simplify(_raw, epsilonMeters / MapPoint.MetersPerUnit);

        // Keep graph nodes reasonably spaced while preserving turns.
        var result = new List<MapPoint>();
        foreach (var p in simplified)
        {
            if (result.Count == 0 || result[^1].DistanceMeters(p) >= 18)
                result.Add(p);
        }

        if (result.Count == 1 && simplified.Count > 1)
            result.Add(simplified[^1]);

        _raw.Clear();
        return result;
    }

    private static List<MapPoint> Simplify(IReadOnlyList<MapPoint> points, double epsilonUnits)
    {
        if (points.Count <= 2) return points.ToList();

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(points, 0, points.Count - 1, epsilonUnits, keep);

        return points.Where((_, i) => keep[i]).ToList();
    }

    private static void SimplifyRange(
        IReadOnlyList<MapPoint> points,
        int first,
        int last,
        double epsilon,
        bool[] keep)
    {
        if (last <= first + 1) return;

        var max = 0.0;
        var index = -1;

        for (var i = first + 1; i < last; i++)
        {
            var d = DistanceToSegment(points[i], points[first], points[last]);
            if (d > max)
            {
                max = d;
                index = i;
            }
        }

        if (index >= 0 && max > epsilon)
        {
            keep[index] = true;
            SimplifyRange(points, first, index, epsilon, keep);
            SimplifyRange(points, index, last, epsilon, keep);
        }
    }

    private static double DistanceToSegment(MapPoint p, MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        if (Math.Abs(dx) + Math.Abs(dy) < 1e-9)
            return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));

        var t = Math.Clamp(
            ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy),
            0,
            1);

        var x = a.X + t * dx;
        var y = a.Y + t * dy;

        return Math.Sqrt(Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2));
    }
}

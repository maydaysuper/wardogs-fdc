namespace WardogsNavigator.Services;

public sealed class NavigationPositionFilter
{
    private readonly Queue<MapPoint> _recent =
        new();

    private MapPoint? _filtered;
    private MapPoint? _lastRaw;
    private DateTime _lastAcceptedUtc;
    private int _rejectedStreak;

    public int AcceptedSamples { get; private set; }
    public int RejectedSamples { get; private set; }
    public double LastSpeedKmh { get; private set; }

    public void Reset()
    {
        _recent.Clear();
        _filtered = null;
        _lastRaw = null;
        _lastAcceptedUtc = default;
        _rejectedStreak = 0;
        AcceptedSamples = 0;
        RejectedSamples = 0;
        LastSpeedKmh = 0;
    }

    public bool TryAccept(
        MapPoint raw,
        DateTime nowUtc,
        out MapPoint filtered)
    {
        filtered = raw;

        if (!raw.IsInsideMap)
        {
            RejectedSamples++;
            return false;
        }

        if (_lastRaw is MapPoint previous &&
            _lastAcceptedUtc != default)
        {
            var dt = Math.Max(
                0.05,
                (nowUtc - _lastAcceptedUtc)
                    .TotalSeconds);

            var jumpMeters =
                previous.DistanceMeters(raw);

            // Very generous physical gate: enough for fast vehicles,
            // but rejects common OCR digit swaps / map jumps.
            var maxJumpMeters =
                Math.Max(
                    85,
                    dt * 85);

            if (jumpMeters > maxJumpMeters)
            {
                _rejectedStreak++;
                RejectedSamples++;

                // Do not permanently lock onto an old coordinate if the
                // source genuinely moved. Require three mutually plausible
                // new samples before allowing the stream to re-anchor.
                if (_rejectedStreak < 3)
                    return false;

                _recent.Clear();
                _filtered = raw;
                _lastRaw = raw;
                _lastAcceptedUtc = nowUtc;
                _rejectedStreak = 0;
                AcceptedSamples++;
                LastSpeedKmh = 0;
                filtered = raw;
                return true;
            }

            LastSpeedKmh =
                jumpMeters / dt * 3.6;
        }

        _rejectedStreak = 0;
        _lastRaw = raw;
        _lastAcceptedUtc = nowUtc;

        _recent.Enqueue(raw);
        while (_recent.Count > 3)
            _recent.Dequeue();

        var median = MedianPoint(_recent);

        if (_filtered is MapPoint current)
        {
            // Strong enough to suppress OCR jitter, weak enough that a
            // moving vehicle does not feel delayed on the HUD.
            const double alpha = 0.82;

            _filtered = new MapPoint(
                current.X +
                (median.X - current.X) * alpha,
                current.Y +
                (median.Y - current.Y) * alpha);
        }
        else
        {
            _filtered = median;
        }

        filtered = _filtered.Value;
        AcceptedSamples++;
        return true;
    }

    private static MapPoint MedianPoint(
        IEnumerable<MapPoint> points)
    {
        var list = points.ToList();
        if (list.Count == 0)
            return default;

        var xs = list
            .Select(x => x.X)
            .OrderBy(x => x)
            .ToArray();

        var ys = list
            .Select(x => x.Y)
            .OrderBy(x => x)
            .ToArray();

        var mid = list.Count / 2;

        if (list.Count % 2 == 1)
            return new MapPoint(
                xs[mid],
                ys[mid]);

        return new MapPoint(
            (xs[mid - 1] + xs[mid]) * 0.5,
            (ys[mid - 1] + ys[mid]) * 0.5);
    }
}

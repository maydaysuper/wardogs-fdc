namespace WardogsNavigator.Services;

/// <summary>
/// Fuses local frame motion, an optional speedometer OCR signal and Road Graph
/// map matching. The result is a continuously estimated vehicle state after
/// one absolute coordinate anchor has been captured.
/// </summary>
public sealed class VisualDrivingTracker
{
    private readonly RoadGraphMapMatcher _matcher = new();

    private MapPoint _position;
    private double? _headingDeg;
    private DateTime _lastUtc;
    private double _lastSpeedKmh;
    private string _edgeId = "";

    public bool IsActive { get; private set; }
    public double CumulativeDistanceMeters { get; private set; }
    public VisualVehicleState LastState { get; private set; } =
        new();

    public void Reset()
    {
        IsActive = false;
        _position = default;
        _headingDeg = null;
        _lastUtc = default;
        _lastSpeedKmh = 0;
        _edgeId = "";
        CumulativeDistanceMeters = 0;
        LastState = new VisualVehicleState();
    }

    public void Start(
        MapPoint anchor,
        double? headingDeg,
        DateTime nowUtc)
    {
        _position = anchor;
        _headingDeg =
            headingDeg.HasValue
                ? RoadGraphMapMatcher
                    .Normalize360(
                        headingDeg.Value)
                : null;
        _lastUtc = nowUtc;
        _lastSpeedKmh = 0;
        _edgeId = "";
        CumulativeDistanceMeters = 0;
        IsActive = true;

        LastState =
            new VisualVehicleState
            {
                Position = anchor,
                HeadingDeg = _headingDeg,
                Confidence = 1,
                SpeedSource = "anchor"
            };
    }

    public void ApplyHeadingHint(
        double? headingDeg,
        double weight = 0.55)
    {
        if (!IsActive ||
            !headingDeg.HasValue)
            return;

        if (!_headingDeg.HasValue)
        {
            _headingDeg =
                RoadGraphMapMatcher
                    .Normalize360(
                        headingDeg.Value);
            return;
        }

        _headingDeg =
            BlendAngle(
                _headingDeg.Value,
                headingDeg.Value,
                Math.Clamp(
                    weight,
                    0,
                    1));
    }

    public VisualVehicleState Update(
        VisualMotionSample motion,
        double? hudSpeedKmh,
        double fallbackVehicleSpeedKmh,
        DateTime nowUtc,
        RoadGraph graph,
        double mapMatchMaxMeters = 140)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException(
                "视觉连续定位尚未用绝对坐标初始化。");
        }

        var dt =
            _lastUtc == default
                ? Math.Max(
                    0.05,
                    motion.DeltaSeconds)
                : Math.Clamp(
                    (nowUtc - _lastUtc)
                        .TotalSeconds,
                    0.05,
                    2.5);

        var hasHudSpeed =
            hudSpeedKmh.HasValue &&
            hudSpeedKmh.Value >= 0 &&
            hudSpeedKmh.Value <= 260;

        double observedSpeed;
        string speedSource;
        double speedConfidence;

        if (hasHudSpeed)
        {
            observedSpeed =
                hudSpeedKmh!.Value;
            speedSource = "HUD";
            speedConfidence = 0.96;
        }
        else if (motion.IsMoving)
        {
            var baseSpeed =
                Math.Clamp(
                    fallbackVehicleSpeedKmh <= 1
                        ? 65
                        : fallbackVehicleSpeedKmh,
                    15,
                    180);

            observedSpeed =
                baseSpeed *
                Math.Clamp(
                    0.22 +
                    motion.Motion01 *
                    0.78,
                    0.12,
                    1.0);

            speedSource = "视觉估算";
            speedConfidence =
                0.20 +
                motion.Confidence *
                0.22;
        }
        else
        {
            observedSpeed = 0;
            speedSource = "视觉静止";
            speedConfidence =
                Math.Clamp(
                    0.35 +
                    motion.Confidence *
                    0.25,
                    0,
                    0.60);
        }

        var alpha =
            hasHudSpeed
                ? 0.55
                : 0.36;

        _lastSpeedKmh =
            _lastSpeedKmh <= 0.5
                ? observedSpeed
                : _lastSpeedKmh +
                  (
                      observedSpeed -
                      _lastSpeedKmh
                  ) *
                  alpha;

        if (_headingDeg.HasValue &&
            motion.Confidence >= 0.08)
        {
            _headingDeg =
                RoadGraphMapMatcher
                    .Normalize360(
                        _headingDeg.Value +
                        motion.YawDeltaDeg *
                        Math.Clamp(
                            0.35 +
                            motion.Confidence *
                            0.40,
                            0.30,
                            0.72));
        }

        var distanceMeters =
            Math.Max(
                0,
                _lastSpeedKmh) /
            3.6 *
            dt;

        // If we only have the visual fallback and the frame says stationary,
        // do not let smoothed speed continue to drift the estimated position.
        if (!hasHudSpeed &&
            !motion.IsMoving)
        {
            distanceMeters = 0;
            _lastSpeedKmh *= 0.35;
        }

        var raw =
            Advance(
                _position,
                _headingDeg ?? 0,
                distanceMeters);

        var mapMatch =
            _matcher.Match(
                graph,
                raw,
                _headingDeg,
                _edgeId,
                mapMatchMaxMeters);

        var corrected = raw;

        if (mapMatch.Success)
        {
            var correctionWeight =
                mapMatch.DistanceMeters <= 30
                    ? 0.82
                    : mapMatch.Confidence >= 0.72
                        ? 0.64
                        : mapMatch.Confidence >= 0.48
                            ? 0.40
                            : 0.22;

            corrected =
                Lerp(
                    raw,
                    mapMatch.ProjectedPoint,
                    correctionWeight);

            _edgeId =
                mapMatch.EdgeId;

            if (_headingDeg.HasValue &&
                mapMatch.Confidence >= 0.48)
            {
                _headingDeg =
                    BlendAngle(
                        _headingDeg.Value,
                        mapMatch.EdgeBearingDeg,
                        0.12 +
                        mapMatch.Confidence *
                        0.16);
            }
            else if (!_headingDeg.HasValue)
            {
                _headingDeg =
                    mapMatch.EdgeBearingDeg;
            }
        }

        if (!corrected.IsInsideMap)
        {
            corrected =
                new MapPoint(
                    Math.Clamp(
                        corrected.X,
                        0,
                        MapPoint.MapSize),
                    Math.Clamp(
                        corrected.Y,
                        0,
                        MapPoint.MapSize));
        }

        _position = corrected;
        _lastUtc = nowUtc;
        CumulativeDistanceMeters +=
            distanceMeters;

        var confidence =
            Math.Clamp(
                speedConfidence *
                0.38 +
                motion.Confidence *
                0.25 +
                (
                    mapMatch.Success
                        ? mapMatch.Confidence
                        : 0.12
                ) *
                0.37,
                0.05,
                0.99);

        LastState =
            new VisualVehicleState
            {
                Position = _position,
                HeadingDeg = _headingDeg,
                SpeedKmh =
                    Math.Max(
                        0,
                        _lastSpeedKmh),
                SpeedSource =
                    speedSource,
                Motion01 =
                    motion.Motion01,
                MotionConfidence =
                    motion.Confidence,
                MapMatchConfidence =
                    mapMatch.Success
                        ? mapMatch.Confidence
                        : 0,
                MapMatchDistanceMeters =
                    mapMatch.Success
                        ? mapMatch.DistanceMeters
                        : double.NaN,
                EdgeId =
                    mapMatch.Success
                        ? mapMatch.EdgeId
                        : _edgeId,
                CumulativeDistanceMeters =
                    CumulativeDistanceMeters,
                Confidence =
                    confidence,
                TimestampUtc =
                    nowUtc
            };

        return LastState;
    }

    private static MapPoint Advance(
        MapPoint start,
        double bearingDeg,
        double meters)
    {
        if (meters <= 0)
            return start;

        var radians =
            bearingDeg *
            Math.PI /
            180.0;

        var east =
            Math.Sin(
                radians) *
            meters;

        var north =
            Math.Cos(
                radians) *
            meters;

        return new MapPoint(
            start.X +
            east /
            MapPoint.MetersPerUnit,
            start.Y +
            north /
            MapPoint.MetersPerUnit);
    }

    private static MapPoint Lerp(
        MapPoint a,
        MapPoint b,
        double weight)
    {
        weight =
            Math.Clamp(
                weight,
                0,
                1);

        return new MapPoint(
            a.X +
            (b.X - a.X) *
            weight,
            a.Y +
            (b.Y - a.Y) *
            weight);
    }

    private static double BlendAngle(
        double from,
        double to,
        double weight)
    {
        var delta =
            NavigationGuidance
                .NormalizeSigned(
                    to - from);

        return RoadGraphMapMatcher
            .Normalize360(
                from +
                delta *
                weight);
    }
}

public sealed class VisualVehicleState
{
    public MapPoint Position { get; set; }
    public double? HeadingDeg { get; set; }
    public double SpeedKmh { get; set; }
    public string SpeedSource { get; set; } = "";
    public double Motion01 { get; set; }
    public double MotionConfidence { get; set; }
    public double MapMatchConfidence { get; set; }
    public double MapMatchDistanceMeters { get; set; }
    public string EdgeId { get; set; } = "";
    public double CumulativeDistanceMeters { get; set; }
    public double Confidence { get; set; }
    public DateTime TimestampUtc { get; set; }
}

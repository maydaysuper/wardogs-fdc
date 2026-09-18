namespace WardogsNavigator.Services;

public static class NavigationGuidance
{
    public static List<NavigationManeuver> BuildManeuvers(
        IReadOnlyList<MapPoint> points)
    {
        var result = new List<NavigationManeuver>();
        if (points.Count == 0) return result;

        var cumulative = BuildCumulative(points);

        result.Add(new NavigationManeuver
        {
            Kind = NavigationManeuverKind.Depart,
            Position = points[0],
            RoutePointIndex = 0,
            DistanceFromStartMeters = 0
        });

        for (var i = 1; i + 1 < points.Count; i++)
        {
            var incoming = points[i - 1].BearingDegTo(points[i]);
            var outgoing = points[i].BearingDegTo(points[i + 1]);
            var delta = NormalizeSigned(outgoing - incoming);
            var abs = Math.Abs(delta);

            if (abs < 22)
                continue;

            var kind = abs switch
            {
                < 50 => delta < 0
                    ? NavigationManeuverKind.SlightLeft
                    : NavigationManeuverKind.SlightRight,
                < 135 => delta < 0
                    ? NavigationManeuverKind.TurnLeft
                    : NavigationManeuverKind.TurnRight,
                _ => NavigationManeuverKind.UTurn
            };

            result.Add(new NavigationManeuver
            {
                Kind = kind,
                Position = points[i],
                RoutePointIndex = i,
                DistanceFromStartMeters = cumulative[i],
                TurnDegrees = delta
            });
        }

        if (points.Count > 1)
        {
            result.Add(new NavigationManeuver
            {
                Kind = NavigationManeuverKind.Arrive,
                Position = points[^1],
                RoutePointIndex = points.Count - 1,
                DistanceFromStartMeters = cumulative[^1]
            });
        }

        return result;
    }

    public static string FormatInstruction(
        NavigationManeuver? maneuver,
        double distanceMeters,
        double desiredBearingDeg,
        bool offRoute)
    {
        if (offRoute)
            return "已偏离路线，正在确认当前位置";

        if (maneuver == null)
            return "沿当前路线继续行驶";

        if (maneuver.Kind == NavigationManeuverKind.Arrive)
        {
            if (distanceMeters <= 35)
                return "已到达目的地";

            return "继续行驶 " +
                   FormatDistance(distanceMeters) +
                   " 到达目的地";
        }

        if (maneuver.Kind == NavigationManeuverKind.Depart)
            return "出发，朝 " +
                   desiredBearingDeg.ToString("000") +
                   "° 沿路线行驶";

        var action = maneuver.Kind switch
        {
            NavigationManeuverKind.SlightLeft => "向左前方行驶",
            NavigationManeuverKind.SlightRight => "向右前方行驶",
            NavigationManeuverKind.TurnLeft => "左转",
            NavigationManeuverKind.TurnRight => "右转",
            NavigationManeuverKind.UTurn => "掉头",
            _ => "继续直行"
        };

        if (distanceMeters <= 28)
            return "现在" + action;

        return "前方 " +
               FormatDistance(distanceMeters) +
               " " +
               action;
    }

    public static string FormatDistance(double meters)
    {
        if (meters < 1000)
            return Math.Max(0, meters).ToString("F0") + " 米";

        return (meters / 1000.0).ToString("F1") + " 公里";
    }

    internal static double[] BuildCumulative(
        IReadOnlyList<MapPoint> points)
    {
        var cumulative = new double[points.Count];

        for (var i = 1; i < points.Count; i++)
        {
            cumulative[i] =
                cumulative[i - 1] +
                points[i - 1].DistanceMeters(points[i]);
        }

        return cumulative;
    }

    internal static double NormalizeSigned(double deg)
    {
        deg %= 360;
        if (deg > 180) deg -= 360;
        if (deg < -180) deg += 360;
        return deg;
    }
}

public sealed class NavigationGuidanceTracker
{
    public const double WarningDeviationMeters = 45;
    public const double RerouteDeviationMeters = 75;
    public const int RequiredOffRouteSamples = 2;

    private RoutePlan? _route;
    private double[] _cumulative = Array.Empty<double>();
    private List<NavigationManeuver> _maneuvers = new();
    private int _lastSegmentIndex;
    private double _lastDistanceFromStartMeters;
    private int _offRouteSamples;

    public void Reset(RoutePlan? route)
    {
        _route = route;
        _lastSegmentIndex = 0;
        _lastDistanceFromStartMeters = 0;
        _offRouteSamples = 0;

        if (route == null || route.Points.Count == 0)
        {
            _cumulative = Array.Empty<double>();
            _maneuvers = new();
            return;
        }

        _cumulative =
            NavigationGuidance.BuildCumulative(route.Points);
        _maneuvers =
            NavigationGuidance.BuildManeuvers(route.Points);
    }

    public NavigationMatch Update(
        MapPoint current,
        double? headingDeg = null)
    {
        if (_route == null ||
            _route.Points.Count == 0)
        {
            return new NavigationMatch
            {
                ProjectedPoint = current,
                DeviationMeters = double.MaxValue,
                OffRoute = true,
                ShouldReroute = false
            };
        }

        if (_route.Points.Count == 1)
        {
            var d = current.DistanceMeters(
                _route.Points[0]);

            return new NavigationMatch
            {
                ProjectedPoint = _route.Points[0],
                DeviationMeters = d,
                RemainingMeters = d,
                OffRoute = d > WarningDeviationMeters,
                ShouldReroute = d > 180
            };
        }

        var best = FindBestMatch(
            current,
            Math.Max(0, _lastSegmentIndex - 2),
            Math.Min(
                _route.Points.Count - 2,
                _lastSegmentIndex + 12),
            headingDeg);

        if (best.DeviationMeters > 140)
        {
            var global = FindBestMatch(
                current,
                0,
                _route.Points.Count - 2,
                headingDeg);

            if (global.DeviationMeters <
                best.DeviationMeters)
                best = global;
        }

        // Prevent noisy OCR from snapping far backward on loops/intersections.
        if (best.DistanceFromStartMeters <
            _lastDistanceFromStartMeters - 45)
        {
            var forward = FindBestMatch(
                current,
                Math.Max(0, _lastSegmentIndex - 1),
                Math.Min(
                    _route.Points.Count - 2,
                    _lastSegmentIndex + 18),
                headingDeg);

            if (forward.DistanceFromStartMeters >=
                _lastDistanceFromStartMeters - 45)
                best = forward;
        }

        _lastSegmentIndex = best.SegmentIndex;
        _lastDistanceFromStartMeters = Math.Max(
            _lastDistanceFromStartMeters - 12,
            best.DistanceFromStartMeters);

        if (best.DeviationMeters >= RerouteDeviationMeters)
            _offRouteSamples++;
        else if (best.DeviationMeters <= WarningDeviationMeters)
            _offRouteSamples = 0;
        else
            _offRouteSamples = Math.Max(0, _offRouteSamples - 1);

        var shouldReroute =
            best.DeviationMeters >= 180 ||
            _offRouteSamples >= RequiredOffRouteSamples;

        var totalMeters =
            _cumulative.Length == 0
                ? 0
                : _cumulative[^1];

        var remainingMeters = Math.Max(
            0,
            totalMeters - best.DistanceFromStartMeters);

        var progress = totalMeters <= 1
            ? 1
            : Math.Clamp(
                best.DistanceFromStartMeters / totalMeters,
                0,
                1);

        var remainingMinutes =
            _route.EstimatedMinutes *
            (1.0 - progress);

        var next = _maneuvers.FirstOrDefault(
            x => x.DistanceFromStartMeters >
                 best.DistanceFromStartMeters + 12)
            ?? _maneuvers.LastOrDefault();

        var distanceToNext = next == null
            ? remainingMeters
            : Math.Max(
                0,
                next.DistanceFromStartMeters -
                best.DistanceFromStartMeters);

        return new NavigationMatch
        {
            ProjectedPoint = best.Projected,
            SegmentIndex = best.SegmentIndex,
            DistanceFromStartMeters =
                best.DistanceFromStartMeters,
            RemainingMeters = remainingMeters,
            DeviationMeters = best.DeviationMeters,
            Progress01 = progress,
            RemainingMinutes = remainingMinutes,
            OffRoute =
                best.DeviationMeters >
                WarningDeviationMeters,
            ShouldReroute = shouldReroute,
            ConsecutiveOffRouteSamples =
                _offRouteSamples,
            NextManeuver = next,
            DistanceToNextManeuverMeters =
                distanceToNext
        };
    }

    public NavigationCue BuildCue(
        MapPoint current,
        double? headingDeg = null)
    {
        var match = Update(
            current,
            headingDeg);

        if (_route == null ||
            _route.Points.Count == 0)
        {
            return new NavigationCue
            {
                Instruction = "无路线",
                OffRoute = true,
                DeviationMeters =
                    match.DeviationMeters
            };
        }

        var destination = _route.Points[^1];
        var toDestination =
            current.DistanceMeters(destination);

        if (toDestination <= 35)
        {
            return new NavigationCue
            {
                Instruction = "已到达目的地",
                Arrived = true,
                RemainingKm =
                    toDestination / 1000.0,
                RemainingMinutes = 0,
                RouteIndex =
                    _route.Points.Count - 1,
                ManeuverRoutePointIndex =
                    _route.Points.Count - 1,
                ManeuverKind =
                    NavigationManeuverKind.Arrive,
                Progress01 = 1,
                DeviationMeters =
                    match.DeviationMeters
            };
        }

        var segmentEndIndex = Math.Min(
            _route.Points.Count - 1,
            match.SegmentIndex + 1);

        var desiredBearing =
            match.ProjectedPoint.BearingDegTo(
                _route.Points[segmentEndIndex]);

        var instruction =
            NavigationGuidance.FormatInstruction(
                match.NextManeuver,
                match.DistanceToNextManeuverMeters,
                desiredBearing,
                match.OffRoute);

        return new NavigationCue
        {
            Instruction = instruction,
            RemainingKm =
                match.RemainingMeters / 1000.0,
            RemainingMinutes =
                match.RemainingMinutes,
            NextDistanceMeters =
                match.DistanceToNextManeuverMeters,
            DesiredBearingDeg = desiredBearing,
            Arrived = false,
            RouteIndex = segmentEndIndex,
            ManeuverRoutePointIndex =
                match.NextManeuver?.RoutePointIndex ?? -1,
            ManeuverKind =
                match.NextManeuver?.Kind ??
                NavigationManeuverKind.Continue,
            Progress01 = match.Progress01,
            DeviationMeters =
                match.DeviationMeters,
            OffRoute = match.OffRoute,
            ShouldReroute =
                match.ShouldReroute
        };
    }

    private SegmentMatch FindBestMatch(
        MapPoint current,
        int startSegment,
        int endSegment,
        double? headingDeg)
    {
        SegmentMatch? best = null;

        for (var i = startSegment;
             i <= endSegment;
             i++)
        {
            var a = _route!.Points[i];
            var b = _route.Points[i + 1];
            var projected =
                Project(current, a, b, out var t);

            var deviation =
                current.DistanceMeters(projected);

            var distanceFromStart =
                _cumulative[i] +
                a.DistanceMeters(b) * t;

            var segmentBearing =
                a.BearingDegTo(b);

            var headingPenalty =
                headingDeg is null
                    ? 0
                    : Math.Abs(
                        NavigationGuidance.NormalizeSigned(
                            segmentBearing -
                            headingDeg.Value)) *
                      0.32;

            var candidate = new SegmentMatch(
                i,
                projected,
                deviation,
                distanceFromStart,
                deviation + headingPenalty);

            if (best == null ||
                candidate.MatchScore <
                best.Value.MatchScore)
                best = candidate;
        }

        return best ??
               new SegmentMatch(
                   0,
                   _route!.Points[0],
                   current.DistanceMeters(
                       _route.Points[0]),
                   0,
                   current.DistanceMeters(
                       _route.Points[0]));
    }

    private static MapPoint Project(
        MapPoint p,
        MapPoint a,
        MapPoint b,
        out double t)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var denom = dx * dx + dy * dy;

        if (denom < 1e-12)
        {
            t = 0;
            return a;
        }

        t = Math.Clamp(
            ((p.X - a.X) * dx +
             (p.Y - a.Y) * dy) /
            denom,
            0,
            1);

        return new MapPoint(
            a.X + t * dx,
            a.Y + t * dy);
    }

    private readonly record struct SegmentMatch(
        int SegmentIndex,
        MapPoint Projected,
        double DeviationMeters,
        double DistanceFromStartMeters,
        double MatchScore);
}

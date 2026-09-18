namespace WardogsNavigator.Services;

/// <summary>
/// Projects a dead-reckoned vehicle position onto the most plausible road
/// edge. Distance, road confidence, previous edge continuity and vehicle
/// heading are all considered so intersections do not cause arbitrary snaps.
/// </summary>
public sealed class RoadGraphMapMatcher
{
    public RoadMapMatch Match(
        RoadGraph graph,
        MapPoint point,
        double? headingDeg,
        string? preferredEdgeId = null,
        double maxDistanceMeters = 140)
    {
        if (graph.Nodes.Count == 0 ||
            graph.Edges.Count == 0)
        {
            return RoadMapMatch.None(point);
        }

        var nodes =
            graph.Nodes
                .GroupBy(
                    x => x.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.First(),
                    StringComparer.OrdinalIgnoreCase);

        RoadMapMatch? best = null;
        var bestScore = double.MaxValue;

        foreach (var edge in graph.Edges)
        {
            if (!nodes.TryGetValue(
                    edge.A,
                    out var a) ||
                !nodes.TryGetValue(
                    edge.B,
                    out var b))
                continue;

            var projected =
                Project(
                    point,
                    a.Position,
                    b.Position,
                    out _);

            var distance =
                point.DistanceMeters(
                    projected);

            if (distance >
                maxDistanceMeters * 1.8)
                continue;

            var ab =
                a.Position.BearingDegTo(
                    b.Position);

            var ba =
                Normalize360(
                    ab + 180);

            var selectedBearing =
                headingDeg.HasValue &&
                AngularDifference(
                    headingDeg.Value,
                    ba) <
                AngularDifference(
                    headingDeg.Value,
                    ab)
                    ? ba
                    : ab;

            var headingPenalty =
                headingDeg.HasValue
                    ? AngularDifference(
                          headingDeg.Value,
                          selectedBearing) /
                      180.0 *
                      55.0
                    : 0;

            var roadPenalty =
                edge.Verified
                    ? 0
                    : edge.Source.Equals(
                        "auto",
                        StringComparison.OrdinalIgnoreCase)
                        ? 22
                        : 10;

            var continuityBonus =
                !string.IsNullOrWhiteSpace(
                    preferredEdgeId) &&
                edge.Id.Equals(
                    preferredEdgeId,
                    StringComparison.OrdinalIgnoreCase)
                    ? 26
                    : 0;

            var score =
                distance +
                headingPenalty +
                roadPenalty -
                continuityBonus;

            if (score >= bestScore)
                continue;

            var proximity =
                Math.Clamp(
                    1 -
                    distance /
                    Math.Max(
                        1,
                        maxDistanceMeters),
                    0,
                    1);

            var headingConfidence =
                !headingDeg.HasValue
                    ? 0.70
                    : Math.Clamp(
                        1 -
                        AngularDifference(
                            headingDeg.Value,
                            selectedBearing) /
                        120.0,
                        0,
                        1);

            var roadConfidence =
                edge.Verified
                    ? 0.95
                    : Math.Clamp(
                        edge.AutoScore > 0
                            ? edge.AutoScore
                            : 0.55,
                        0.35,
                        0.85);

            bestScore = score;
            best =
                new RoadMapMatch
                {
                    Success = true,
                    EdgeId = edge.Id,
                    ProjectedPoint =
                        projected,
                    DistanceMeters =
                        distance,
                    EdgeBearingDeg =
                        selectedBearing,
                    Confidence =
                        Math.Clamp(
                            proximity *
                            0.52 +
                            headingConfidence *
                            0.23 +
                            roadConfidence *
                            0.25,
                            0,
                            1)
                };
        }

        if (best == null ||
            best.DistanceMeters >
            maxDistanceMeters)
        {
            return RoadMapMatch.None(
                point);
        }

        return best;
    }

    private static MapPoint Project(
        MapPoint point,
        MapPoint a,
        MapPoint b,
        out double t)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length2 =
            dx * dx +
            dy * dy;

        if (length2 < 1e-12)
        {
            t = 0;
            return a;
        }

        t =
            (
                (point.X - a.X) * dx +
                (point.Y - a.Y) * dy
            ) /
            length2;

        t = Math.Clamp(
            t,
            0,
            1);

        return new MapPoint(
            a.X + dx * t,
            a.Y + dy * t);
    }

    internal static double AngularDifference(
        double a,
        double b)
    {
        var diff =
            Math.Abs(
                Normalize360(a) -
                Normalize360(b));

        return diff > 180
            ? 360 - diff
            : diff;
    }

    internal static double Normalize360(
        double value)
    {
        value %= 360;
        if (value < 0)
            value += 360;
        return value;
    }
}

public sealed class RoadMapMatch
{
    public bool Success { get; set; }
    public string EdgeId { get; set; } = "";
    public MapPoint ProjectedPoint { get; set; }
    public double DistanceMeters { get; set; }
    public double EdgeBearingDeg { get; set; }
    public double Confidence { get; set; }

    public static RoadMapMatch None(
        MapPoint point) =>
        new()
        {
            ProjectedPoint = point
        };
}

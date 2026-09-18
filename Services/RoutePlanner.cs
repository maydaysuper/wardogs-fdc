using SixLabors.ImageSharp.PixelFormats;

namespace WardogsNavigator.Services;

/// <summary>
/// Road-aware raster A*. It derives local travel cost from the real tactical map image.
/// It never calls EconomyEngine or AI: economy chooses the plan, this class only chooses the path.
/// </summary>
public sealed class RoutePlanner
{
    private readonly MapAssetService _assets;
    private readonly RoadGraphStore _roadGraphs;
    private readonly NavigationHazardStore _hazards;
    private readonly NavigationVisionEvidenceStore _visionEvidence;
    private readonly RoadGraphRouter _graphRouter = new();
    private readonly Dictionary<string, float[]> _costCache = new(StringComparer.OrdinalIgnoreCase);
    private const int Grid = 384;

    public RoutePlanner(
        MapAssetService assets,
        RoadGraphStore? roadGraphs = null,
        NavigationHazardStore? hazards = null,
        NavigationVisionEvidenceStore? visionEvidence = null)
    {
        _assets = assets;
        _roadGraphs = roadGraphs ?? new RoadGraphStore();
        _hazards = hazards ?? new NavigationHazardStore();
        _visionEvidence =
            visionEvidence ??
            new NavigationVisionEvidenceStore();
    }

    public async Task<RoutePlan> PlanAsync(
        string mapId,
        MapPoint start,
        MapPoint end,
        RoutePreference preference,
        double speedKmh,
        bool air,
        VehicleRoutingProfile? vehicleProfile = null,
        CancellationToken cancellationToken = default)
    {
        vehicleProfile ??= VehicleRoutingProfileService.Generic();

        if (air)
            return Straight(
                mapId,
                start,
                end,
                preference,
                speedKmh,
                "air-direct",
                vehicleProfile.VehicleId);

        try
        {
            var graph = _roadGraphs.Load(mapId);
            var activeHazards = _hazards.GetActive(mapId);
            var visualRisks = _visionEvidence.GetRiskMap(mapId);

            var graphRoute = _graphRouter.TryPlan(
                graph,
                start,
                end,
                preference,
                vehicleProfile,
                activeHazards,
                visualRisks,
                speedKmh);

            if (graphRoute != null &&
                graphRoute.Points.Count >= 2 &&
                graphRoute.Confidence >= 0.18)
            {
                return new RoutePlan
                {
                    MapId = mapId,
                    Preference = preference,
                    Points = graphRoute.Points,
                    DistanceKm = graphRoute.DistanceKm,
                    EstimatedMinutes = graphRoute.EstimatedMinutes,
                    Source =
                        (graphRoute.Confidence >= 0.50
                            ? "road-graph verified "
                            : "road-graph auto ") +
                        Math.Round(graphRoute.Confidence * 100).ToString("F0") +
                        "% · " +
                        graphRoute.EdgeCount +
                        " edges" +
                        (activeHazards.Count > 0
                            ? " · hazards " + activeHazards.Count
                            : "") +
                        (visualRisks.Count > 0
                            ? " · vision " + visualRisks.Count
                            : ""),
                    VehicleProfileId = vehicleProfile.VehicleId,
                    EdgeIds = graphRoute.EdgeIds
                };
            }

            var mapPath = await _assets.GetMapWebpPathAsync(mapId, cancellationToken);
            var costs = GetOrBuildCosts(mapPath, mapId, preference);
            var points = await Task.Run(() => AStar(costs, start, end, cancellationToken), cancellationToken);
            if (points.Count < 2)
                return Straight(
                    mapId,
                    start,
                    end,
                    preference,
                    speedKmh,
                    "fallback-direct",
                    vehicleProfile.VehicleId,
                    true);

            var simplified = Simplify(points, 0.18);
            var km = PolylineKm(simplified);
            return new RoutePlan
            {
                MapId = mapId,
                Preference = preference,
                Points = simplified,
                DistanceKm = km,
                EstimatedMinutes = speedKmh > 1 ? km / speedKmh * 60.0 : 0,
                Source = "map-raster-a-star",
                VehicleProfileId = vehicleProfile.VehicleId
            };
        }
        catch
        {
            return Straight(
                mapId,
                start,
                end,
                preference,
                speedKmh,
                "fallback-direct",
                vehicleProfile.VehicleId,
                true);
        }
    }

    public static NavigationCue BuildCue(RoutePlan route, MapPoint current, double? headingDeg = null)
    {
        if (route.Points.Count == 0)
            return new NavigationCue { Instruction = "无路线" };

        var destination = route.Points[^1];
        var toDest = current.DistanceMeters(destination);
        if (toDest <= 35)
        {
            return new NavigationCue
            {
                Instruction = "已到达目的地",
                Arrived = true,
                RemainingKm = toDest / 1000.0,
                RouteIndex = route.Points.Count - 1
            };
        }

        var nearest = 0;
        var best = double.MaxValue;
        for (var i = 0; i < route.Points.Count; i++)
        {
            var d = current.DistanceMeters(route.Points[i]);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        var nextIndex = Math.Min(route.Points.Count - 1, nearest + 1);
        while (nextIndex < route.Points.Count - 1 &&
               current.DistanceMeters(route.Points[nextIndex]) < 90)
            nextIndex++;

        var next = route.Points[nextIndex];
        var bearing = current.BearingDegTo(next);
        var nextMeters = current.DistanceMeters(next);

        var remaining = current.DistanceKm(next);
        for (var i = nextIndex; i < route.Points.Count - 1; i++)
            remaining += route.Points[i].DistanceKm(route.Points[i + 1]);

        string action;
        if (headingDeg is null)
        {
            action = "朝 " + bearing.ToString("000") + "° 行驶";
        }
        else
        {
            var delta = NormalizeSigned(bearing - headingDeg.Value);
            action = Math.Abs(delta) < 15 ? "保持方向" :
                delta > 0 ? "向右转 " + Math.Abs(delta).ToString("F0") + "°" :
                "向左转 " + Math.Abs(delta).ToString("F0") + "°";
        }

        return new NavigationCue
        {
            Instruction = action + "，" + nextMeters.ToString("F0") + " 米",
            RemainingKm = remaining,
            NextDistanceMeters = nextMeters,
            DesiredBearingDeg = bearing,
            RouteIndex = nextIndex
        };
    }

    public static double DistanceToRouteMeters(RoutePlan route, MapPoint point)
    {
        if (route.Points.Count == 0) return double.MaxValue;
        if (route.Points.Count == 1) return point.DistanceMeters(route.Points[0]);

        var best = double.MaxValue;
        for (var i = 0; i + 1 < route.Points.Count; i++)
            best = Math.Min(best, DistanceToSegmentMeters(point, route.Points[i], route.Points[i + 1]));
        return best;
    }

    private static double DistanceToSegmentMeters(MapPoint p, MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var denom = dx * dx + dy * dy;

        if (denom < 1e-12)
            return p.DistanceMeters(a);

        var t = Math.Clamp(
            ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / denom,
            0,
            1);

        var projected = new MapPoint(
            a.X + t * dx,
            a.Y + t * dy);

        return p.DistanceMeters(projected);
    }

    private float[] GetOrBuildCosts(string path, string mapId, RoutePreference preference)
    {
        var key = mapId + ":" + preference;
        lock (_costCache)
        {
            if (_costCache.TryGetValue(key, out var cached)) return cached;
        }

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        var result = new float[Grid * Grid];

        for (var gy = 0; gy < Grid; gy++)
        {
            var py = Math.Clamp((int)((gy + 0.5) / Grid * image.Height), 0, image.Height - 1);
            for (var gx = 0; gx < Grid; gx++)
            {
                var px = Math.Clamp((int)((gx + 0.5) / Grid * image.Width), 0, image.Width - 1);
                var p = image[px, py];
                result[gy * Grid + gx] = TerrainCost(p, preference);
            }
        }

        lock (_costCache)
            _costCache[key] = result;
        return result;
    }

    private static float TerrainCost(Rgba32 p, RoutePreference preference)
    {
        var r = p.R / 255f;
        var g = p.G / 255f;
        var b = p.B / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var sat = max - min;
        var lum = (r + g + b) / 3f;

        // Roads on the current tactical maps are generally more neutral and medium-bright than terrain.
        // We keep every cell traversable, but make road-looking pixels cheaper.
        var neutral = 1f - Math.Clamp(sat / 0.35f, 0f, 1f);
        var midBright = 1f - Math.Clamp(Math.Abs(lum - 0.58f) / 0.58f, 0f, 1f);
        var roadScore = Math.Clamp(neutral * 0.65f + midBright * 0.35f, 0f, 1f);

        var waterLike = b > r * 1.12f && b > g * 1.08f ? 1f : 0f;
        var dark = lum < 0.16f ? 1f : 0f;

        return preference switch
        {
            RoutePreference.Shortest => 1f + waterLike * 2.5f + dark * 1.0f + (1f - roadScore) * 0.35f,
            RoutePreference.Safe => 1f + (1f - roadScore) * 2.6f + waterLike * 9f + dark * 3.5f,
            _ => 1f + (1f - roadScore) * 4.2f + waterLike * 6f + dark * 2f
        };
    }

    private static List<MapPoint> AStar(float[] costs, MapPoint start, MapPoint end, CancellationToken ct)
    {
        var s = ToCell(start);
        var e = ToCell(end);
        var total = Grid * Grid;
        var gScore = new double[total];
        Array.Fill(gScore, double.PositiveInfinity);
        var came = new int[total];
        Array.Fill(came, -1);
        var closed = new bool[total];

        var startId = Id(s.X, s.Y);
        var endId = Id(e.X, e.Y);
        gScore[startId] = 0;

        var open = new PriorityQueue<int, double>();
        open.Enqueue(startId, Heuristic(s.X, s.Y, e.X, e.Y));

        var dirs = new (int dx, int dy, double mul)[]
        {
            (1,0,1),(-1,0,1),(0,1,1),(0,-1,1),
            (1,1,1.4142),(1,-1,1.4142),(-1,1,1.4142),(-1,-1,1.4142)
        };

        while (open.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = open.Dequeue();
            if (closed[current]) continue;
            if (current == endId) break;
            closed[current] = true;

            var cx = current % Grid;
            var cy = current / Grid;
            foreach (var d in dirs)
            {
                var nx = cx + d.dx;
                var ny = cy + d.dy;
                if ((uint)nx >= Grid || (uint)ny >= Grid) continue;
                var nid = Id(nx, ny);
                if (closed[nid]) continue;

                var move = d.mul * (costs[current] + costs[nid]) * 0.5;
                var tentative = gScore[current] + move;
                if (tentative >= gScore[nid]) continue;

                came[nid] = current;
                gScore[nid] = tentative;
                var f = tentative + Heuristic(nx, ny, e.X, e.Y);
                open.Enqueue(nid, f);
            }
        }

        if (startId != endId && came[endId] < 0)
            return new List<MapPoint>();

        var cells = new List<int>();
        var cur = endId;
        cells.Add(cur);
        while (cur != startId)
        {
            cur = came[cur];
            if (cur < 0) return new List<MapPoint>();
            cells.Add(cur);
        }
        cells.Reverse();

        return cells.Select(id =>
        {
            var x = id % Grid;
            var y = id / Grid;
            return FromCell(x, y);
        }).ToList();
    }

    private static (int X, int Y) ToCell(MapPoint p)
    {
        var x = Math.Clamp((int)Math.Round(p.X / MapPoint.MapSize * (Grid - 1)), 0, Grid - 1);
        var y = Math.Clamp((int)Math.Round((MapPoint.MapSize - p.Y) / MapPoint.MapSize * (Grid - 1)), 0, Grid - 1);
        return (x, y);
    }

    private static MapPoint FromCell(int x, int y) =>
        new MapPoint(
            x / (double)(Grid - 1) * MapPoint.MapSize,
            MapPoint.MapSize - y / (double)(Grid - 1) * MapPoint.MapSize);

    private static int Id(int x, int y) => y * Grid + x;

    private static double Heuristic(int x, int y, int ex, int ey)
    {
        var dx = ex - x;
        var dy = ey - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static RoutePlan Straight(
        string mapId,
        MapPoint a,
        MapPoint b,
        RoutePreference preference,
        double speedKmh,
        string source,
        string vehicleProfileId,
        bool fallback = false)
    {
        var km = a.DistanceKm(b);
        return new RoutePlan
        {
            MapId = mapId,
            Preference = preference,
            Points = new List<MapPoint> { a, b },
            DistanceKm = km,
            EstimatedMinutes = speedKmh > 1 ? km / speedKmh * 60 : 0,
            Source = source,
            VehicleProfileId = vehicleProfileId,
            UsedFallback = fallback
        };
    }

    private static double PolylineKm(IReadOnlyList<MapPoint> pts)
    {
        double total = 0;
        for (var i = 0; i + 1 < pts.Count; i++)
            total += pts[i].DistanceKm(pts[i + 1]);
        return total;
    }

    private static List<MapPoint> Simplify(List<MapPoint> pts, double epsilonUnits)
    {
        if (pts.Count <= 2) return pts;
        var keep = new bool[pts.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(pts, 0, pts.Count - 1, epsilonUnits, keep);
        return pts.Where((_, i) => keep[i]).ToList();
    }

    private static void SimplifyRange(List<MapPoint> pts, int a, int b, double eps, bool[] keep)
    {
        if (b <= a + 1) return;
        var max = 0.0;
        var idx = -1;
        for (var i = a + 1; i < b; i++)
        {
            var d = PointSegmentUnits(pts[i], pts[a], pts[b]);
            if (d > max)
            {
                max = d;
                idx = i;
            }
        }

        if (idx >= 0 && max > eps)
        {
            keep[idx] = true;
            SimplifyRange(pts, a, idx, eps, keep);
            SimplifyRange(pts, idx, b, eps, keep);
        }
    }

    private static double PointSegmentUnits(MapPoint p, MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 1e-9)
            return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        var qx = a.X + t * dx;
        var qy = a.Y + t * dy;
        return Math.Sqrt(Math.Pow(p.X - qx, 2) + Math.Pow(p.Y - qy, 2));
    }

    private static double NormalizeSigned(double deg)
    {
        deg %= 360;
        if (deg > 180) deg -= 360;
        if (deg < -180) deg += 360;
        return deg;
    }
}

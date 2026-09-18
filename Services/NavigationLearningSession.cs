namespace WardogsNavigator.Services;

public sealed class NavigationLearningSession
{
    private readonly List<MapPoint> _points = new();
    private readonly Dictionary<string, EdgeGeometry> _edgeGeometry =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdgeTravelObservation> _edgeObservations =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTime _startedUtc;
    private DateTime _lastPointUtc;
    private MapPoint? _lastPoint;
    private RoutePlan? _route;
    private RoadGraph? _graph;
    private string _vehicleId = "";
    private double _vehicleBaseSpeedKmh = 80;
    private RoutePreference _preference;
    private string _mapId = "";
    private int _replans;
    private double _maxDeviation;

    public bool IsActive { get; private set; }
    public string VehicleId => _vehicleId;
    public RoutePreference Preference => _preference;

    public void Start(
        string mapId,
        string vehicleId,
        RoutePreference preference,
        RoutePlan route,
        RoadGraph? graph = null,
        MapPoint? initial = null,
        double vehicleBaseSpeedKmh = 80)
    {
        Reset();

        _startedUtc = DateTime.UtcNow;
        _lastPointUtc = _startedUtc;
        _route = route;
        _graph = graph;
        _vehicleId = vehicleId;
        _vehicleBaseSpeedKmh = Math.Max(1, vehicleBaseSpeedKmh);
        _preference = preference;
        _mapId = mapId;
        _replans = 0;
        _maxDeviation = 0;
        IsActive = true;

        RebuildEdgeGeometry();

        if (initial is MapPoint point)
        {
            _points.Add(point);
            _lastPoint = point;
        }
    }

    public void NotePoint(MapPoint point)
    {
        if (!IsActive) return;

        var now = DateTime.UtcNow;
        var deviation = _route == null
            ? 0
            : RoutePlanner.DistanceToRouteMeters(_route, point);

        if (double.IsFinite(deviation))
            _maxDeviation = Math.Max(_maxDeviation, deviation);

        if (_lastPoint is MapPoint previous)
        {
            var movedMeters = previous.DistanceMeters(point);

            if (movedMeters >= 3 && movedMeters <= 260)
            {
                _points.Add(point);

                var nearest = FindNearestTrackedEdge(point);
                if (nearest != null && nearest.DistanceMeters <= 180)
                {
                    if (!_edgeObservations.TryGetValue(
                            nearest.EdgeId,
                            out var observation))
                    {
                        observation = new EdgeTravelObservation
                        {
                            EdgeId = nearest.EdgeId
                        };
                        _edgeObservations[nearest.EdgeId] = observation;
                    }

                    observation.Samples++;
                    observation.DistanceKm += previous.DistanceKm(point);
                    observation.Seconds += Math.Max(
                        0.01,
                        (now - _lastPointUtc).TotalSeconds);
                    observation.MaxDeviationMeters = Math.Max(
                        observation.MaxDeviationMeters,
                        nearest.DistanceMeters);
                }
            }
        }
        else
        {
            _points.Add(point);
        }

        _lastPoint = point;
        _lastPointUtc = now;
    }

    public void NoteReplan(RoutePlan route)
    {
        if (!IsActive) return;

        _route = route;
        _replans++;
        RebuildEdgeGeometry();
    }

    public NavigationExperience? Stop(bool completed)
    {
        if (!IsActive || _route == null)
        {
            Reset();
            return null;
        }

        var end = DateTime.UtcNow;
        double actualKm = 0;

        for (var i = 0; i + 1 < _points.Count; i++)
            actualKm += _points[i].DistanceKm(_points[i + 1]);

        var experience = new NavigationExperience
        {
            StartedUtc = _startedUtc,
            EndedUtc = end,
            MapId = _mapId,
            VehicleId = _vehicleId,
            VehicleBaseSpeedKmh = _vehicleBaseSpeedKmh,
            Preference = _preference,
            Completed = completed,
            PlannedDistanceKm = _route.DistanceKm,
            ActualDistanceKm = actualKm,
            DurationMinutes = Math.Max(0, (end - _startedUtc).TotalMinutes),
            Replans = _replans,
            MaxDeviationMeters = _maxDeviation,
            EdgeIds = _route.EdgeIds
                .Concat(_edgeObservations.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EdgeObservations = _edgeObservations.Values
                .OrderBy(x => x.EdgeId)
                .ToList()
        };

        Reset();
        return experience;
    }

    private void RebuildEdgeGeometry()
    {
        _edgeGeometry.Clear();

        if (_route == null || _graph == null || _route.EdgeIds.Count == 0)
            return;

        var nodes = _graph.Nodes.ToDictionary(
            n => n.Id,
            StringComparer.OrdinalIgnoreCase);

        var routeEdgeIds = _route.EdgeIds.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in _graph.Edges)
        {
            if (!routeEdgeIds.Contains(edge.Id))
                continue;

            if (!nodes.TryGetValue(edge.A, out var a) ||
                !nodes.TryGetValue(edge.B, out var b))
                continue;

            _edgeGeometry[edge.Id] = new EdgeGeometry(
                edge.Id,
                a.Position,
                b.Position);
        }
    }

    private EdgeHit? FindNearestTrackedEdge(MapPoint point)
    {
        EdgeHit? best = null;

        foreach (var edge in _edgeGeometry.Values)
        {
            var distance = DistanceToSegmentMeters(
                point,
                edge.A,
                edge.B);

            if (best == null || distance < best.DistanceMeters)
            {
                best = new EdgeHit(
                    edge.EdgeId,
                    distance);
            }
        }

        return best;
    }

    private static double DistanceToSegmentMeters(
        MapPoint p,
        MapPoint a,
        MapPoint b)
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

    private void Reset()
    {
        IsActive = false;
        _points.Clear();
        _edgeGeometry.Clear();
        _edgeObservations.Clear();
        _route = null;
        _graph = null;
        _lastPoint = null;
        _lastPointUtc = default;
        _vehicleBaseSpeedKmh = 80;
    }

    private readonly record struct EdgeGeometry(
        string EdgeId,
        MapPoint A,
        MapPoint B);

    private sealed record EdgeHit(
        string EdgeId,
        double DistanceMeters);
}

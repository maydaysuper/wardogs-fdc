namespace WardogsNavigator.Services;

public sealed class NavigationLearningSession
{
    private readonly List<MapPoint> _points = new();
    private DateTime _startedUtc;
    private RoutePlan? _route;
    private string _vehicleId = "";
    private RoutePreference _preference;
    private string _mapId = "";
    private int _replans;
    private double _maxDeviation;

    public bool IsActive { get; private set; }

    public void Start(
        string mapId,
        string vehicleId,
        RoutePreference preference,
        RoutePlan route,
        MapPoint? initial = null)
    {
        _points.Clear();
        _startedUtc = DateTime.UtcNow;
        _route = route;
        _vehicleId = vehicleId;
        _preference = preference;
        _mapId = mapId;
        _replans = 0;
        _maxDeviation = 0;
        IsActive = true;

        if (initial is MapPoint point)
            _points.Add(point);
    }

    public void NotePoint(MapPoint point)
    {
        if (!IsActive) return;

        if (_points.Count == 0 ||
            _points[^1].DistanceMeters(point) >= 3)
            _points.Add(point);

        if (_route != null)
        {
            var deviation = RoutePlanner.DistanceToRouteMeters(_route, point);
            if (double.IsFinite(deviation))
                _maxDeviation = Math.Max(_maxDeviation, deviation);
        }
    }

    public void NoteReplan(RoutePlan route)
    {
        if (!IsActive) return;
        _route = route;
        _replans++;
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
            Preference = _preference,
            Completed = completed,
            PlannedDistanceKm = _route.DistanceKm,
            ActualDistanceKm = actualKm,
            DurationMinutes = Math.Max(0, (end - _startedUtc).TotalMinutes),
            Replans = _replans,
            MaxDeviationMeters = _maxDeviation,
            EdgeIds = _route.EdgeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        Reset();
        return experience;
    }

    private void Reset()
    {
        IsActive = false;
        _points.Clear();
        _route = null;
    }
}

namespace WardogsNavigator;

public enum NavigationManeuverKind
{
    Depart,
    Continue,
    SlightLeft,
    SlightRight,
    TurnLeft,
    TurnRight,
    UTurn,
    Arrive
}

public sealed class NavigationManeuver
{
    public NavigationManeuverKind Kind { get; set; }
    public MapPoint Position { get; set; }
    public int RoutePointIndex { get; set; }
    public double DistanceFromStartMeters { get; set; }
    public double TurnDegrees { get; set; }
}

public sealed class NavigationMatch
{
    public MapPoint ProjectedPoint { get; set; }
    public int SegmentIndex { get; set; }
    public double DistanceFromStartMeters { get; set; }
    public double RemainingMeters { get; set; }
    public double DeviationMeters { get; set; }
    public double Progress01 { get; set; }
    public double RemainingMinutes { get; set; }
    public bool OffRoute { get; set; }
    public bool ShouldReroute { get; set; }
    public int ConsecutiveOffRouteSamples { get; set; }
    public NavigationManeuver? NextManeuver { get; set; }
    public double DistanceToNextManeuverMeters { get; set; }
}

namespace WardogsNavigator;

public readonly record struct MapPoint(double X, double Y)
{
    public const double MapSize = 163.84;
    public const double MetersPerUnit = 100.0;

    public double DistanceMeters(MapPoint other)
    {
        var dx = (other.X - X) * MetersPerUnit;
        var dy = (other.Y - Y) * MetersPerUnit;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public double DistanceKm(MapPoint other) => DistanceMeters(other) / 1000.0;

    public double BearingDegTo(MapPoint other)
    {
        var east = other.X - X;
        var north = other.Y - Y;
        if (Math.Abs(east) < 1e-9 && Math.Abs(north) < 1e-9) return 0;
        var deg = Math.Atan2(east, north) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360.0 : deg;
    }

    public bool IsInsideMap => X >= 0 && X <= MapSize && Y >= 0 && Y <= MapSize;

    public override string ToString() => $"X {X:F2} / Y {Y:F2}";
}

public enum DestinationKind { Field, Zone, Fob }
public enum LoadMode { Cargo, Taxi, Mixed }
public enum RoutePreference { Fastest, Shortest, Safe }

public sealed class VehicleSpec
{
    public string Id { get; set; } = "";
    public string NameZh { get; set; } = "";
    public double Price { get; set; }
    public double SpeedKmh { get; set; }
    public int Passengers { get; set; }
    public int PalletSlots { get; set; }
    public double FuelL { get; set; }
    public double RangeKm { get; set; }
    public bool Air { get; set; }
    public bool Armed { get; set; }
}

public sealed class Destination
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public DestinationKind Kind { get; set; }
    public MapPoint Position { get; set; }
}

public sealed class EconomicPlan
{
    public VehicleSpec Vehicle { get; set; } = new();
    public Destination Destination { get; set; } = new();
    public LoadMode LoadMode { get; set; }
    public bool RoundTrip { get; set; }
    public int Pallets { get; set; }
    public int Passengers { get; set; }
    public double DirectDistanceKm { get; set; }
    public double EstimatedMinutes { get; set; }
    public double FirstTripNet { get; set; }
    public double SessionNet { get; set; }
    public double SessionPerMinute { get; set; }
    public int? BreakEvenTrips { get; set; }
}

public sealed class RoutePlan
{
    public string MapId { get; set; } = "";
    public RoutePreference Preference { get; set; }
    public List<MapPoint> Points { get; set; } = new();
    public double DistanceKm { get; set; }
    public double EstimatedMinutes { get; set; }
    public bool UsedFallback { get; set; }
    public string Source { get; set; } = "";
    public string VehicleProfileId { get; set; } = "";
    public List<string> EdgeIds { get; set; } = new();
}

public sealed class NavigationCue
{
    public string Instruction { get; set; } = "";
    public double RemainingKm { get; set; }
    public double NextDistanceMeters { get; set; }
    public double DesiredBearingDeg { get; set; }
    public bool Arrived { get; set; }
    public int RouteIndex { get; set; }
}

public sealed class FireSolution
{
    public double DistanceMeters { get; set; }
    public double AzimuthDeg { get; set; }
    public double DirectionMils { get; set; }
}

public sealed class OcrCoordinateResult
{
    public bool Success { get; set; }
    public MapPoint Point { get; set; }
    public string RawText { get; set; } = "";
    public string Error { get; set; } = "";
}

public sealed class MapMarker
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public MapPoint Position { get; set; }
}

public sealed class MapDefinition
{
    public string Id { get; set; } = "";
    public List<MapMarker> Markers { get; set; } = new();
}

namespace WardogsNavigator;

public sealed class RouteQualityProfile
{
    public string Key { get; set; } = "";
    public string MapId { get; set; } = "";
    public string VehicleId { get; set; } = "";
    public RoutePreference Preference { get; set; }
    public string RouteSignature { get; set; } = "";
    public int Samples { get; set; }
    public int CompletedSamples { get; set; }
    public double EtaRatioEwma { get; set; } = 1.0;
    public double ReplansEwma { get; set; }
    public double MaxDeviationEwma { get; set; }
    public double Reliability { get; set; } = 0.50;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RouteQualityAssessment
{
    public int Samples { get; set; }
    public double Reliability { get; set; } = 0.50;
    public double EtaMultiplier { get; set; } = 1.0;
    public double ExpectedReplans { get; set; }
    public double ExpectedMaxDeviationMeters { get; set; }
}

public sealed class PredictiveRouteCandidate
{
    public string MapId { get; set; } = "";
    public MapPoint Start { get; set; }
    public MapPoint Target { get; set; }
    public RoutePlan Route { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public double TriggerDeviationMeters { get; set; }
}

public sealed class JunctionVisualCheck
{
    public bool Available { get; set; }
    public MapPoint Position { get; set; }
    public double Confidence { get; set; }
    public double RouteSignal { get; set; }
    public double BackgroundSignal { get; set; }
    public string Summary { get; set; } = "";
}

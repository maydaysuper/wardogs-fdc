namespace WardogsNavigator;

public sealed class VehicleRoutingProfile
{
    public string VehicleId { get; set; } = "generic-ground";
    public string Label { get; set; } = "通用地面车辆";
    public double PrimaryFactor { get; set; } = 1.0;
    public double SecondaryFactor { get; set; } = 0.82;
    public double TrackFactor { get; set; } = 0.58;
    public double BridgeFactor { get; set; } = 0.90;
    public double RiskTolerance { get; set; } = 0.50;

    public double FactorFor(RoadClass roadClass) => roadClass switch
    {
        RoadClass.Primary => PrimaryFactor,
        RoadClass.Secondary => SecondaryFactor,
        RoadClass.Track => TrackFactor,
        RoadClass.Bridge => BridgeFactor,
        _ => SecondaryFactor
    };
}

public sealed class NavigationHazard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MapId { get; set; } = "";
    public string Label { get; set; } = "临时危险区";
    public MapPoint Center { get; set; }
    public double RadiusMeters { get; set; } = 300;
    public double Severity { get; set; } = 0.7;
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);
    public string Source { get; set; } = "manual";
}

public sealed class NavigationExperience
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public string MapId { get; set; } = "";
    public string VehicleId { get; set; } = "";
    public RoutePreference Preference { get; set; }
    public bool Completed { get; set; }
    public double PlannedDistanceKm { get; set; }
    public double ActualDistanceKm { get; set; }
    public double DurationMinutes { get; set; }
    public int Replans { get; set; }
    public double MaxDeviationMeters { get; set; }
    public List<string> EdgeIds { get; set; } = new();
}

public sealed class AiRoadSuggestion
{
    public string EdgeId { get; set; } = "";
    public string VehicleId { get; set; } = "";
    public double RiskDelta { get; set; }
    public double SpeedMultiplier { get; set; } = 1.0;
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AiNavigationLearningReport
{
    public string Summary { get; set; } = "";
    public List<AiRoadSuggestion> Suggestions { get; set; } = new();
}

public sealed class NavigationLearningSnapshot
{
    public string MapId { get; set; } = "";
    public int ExperienceCount { get; set; }
    public int CompletedCount { get; set; }
    public double AverageReplans { get; set; }
    public double AverageMaxDeviationMeters { get; set; }
    public List<NavigationExperience> Recent { get; set; } = new();
}

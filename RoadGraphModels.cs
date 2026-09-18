namespace WardogsNavigator;

public enum RoadClass
{
    Primary,
    Secondary,
    Track,
    Bridge
}

public sealed class RoadNode
{
    public string Id { get; set; } = "";
    public MapPoint Position { get; set; }
}

public sealed class RoadEdge
{
    public string Id { get; set; } = "";
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public RoadClass Class { get; set; } = RoadClass.Secondary;
    public bool Blocked { get; set; }
    public bool Verified { get; set; } = true;
    public double Risk { get; set; }
    public int Traversals { get; set; }
    public double AutoScore { get; set; }
    public Dictionary<string, double> VehicleSpeedMultipliers { get; set; } = new();
    public double AiConfidence { get; set; }
    public string AiNote { get; set; } = "";
    public DateTime? AiUpdatedUtc { get; set; }
    public string Source { get; set; } = "manual";
}

public sealed class RoadGraph
{
    public int Version { get; set; } = 2;
    public string MapId { get; set; } = "";
    public List<RoadNode> Nodes { get; set; } = new();
    public List<RoadEdge> Edges { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RoadGraphStats
{
    public int Nodes { get; set; }
    public int Edges { get; set; }
    public int VerifiedEdges { get; set; }
    public int LearnedEdges { get; set; }
    public int AutoEdges { get; set; }
    public double NetworkKm { get; set; }
}

public sealed class RoadGraphRoute
{
    public List<MapPoint> Points { get; set; } = new();
    public double DistanceKm { get; set; }
    public double StartSnapMeters { get; set; }
    public double EndSnapMeters { get; set; }
    public int EdgeCount { get; set; }
    public double Confidence { get; set; }
    public List<string> EdgeIds { get; set; } = new();
}

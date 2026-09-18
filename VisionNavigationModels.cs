namespace WardogsNavigator;

public sealed class AiVisionFinding
{
    public string EdgeId { get; set; } = "";
    public string Kind { get; set; } = "uncertain";
    public double Severity { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AiVisionNavigationReport
{
    public string Summary { get; set; } = "";
    public List<AiVisionFinding> Findings { get; set; } = new();
}

public sealed class VisionEdgeEvidence
{
    public string MapId { get; set; } = "";
    public string EdgeId { get; set; } = "";
    public string Kind { get; set; } = "uncertain";
    public double Severity { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddMinutes(8);
}

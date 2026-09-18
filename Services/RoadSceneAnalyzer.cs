using System.Drawing;

namespace WardogsNavigator.Services;

/// <summary>
/// Stable boundary for a future local semantic/depth ONNX model. v0.11 keeps
/// real-time navigation local and exposes the frame contract now, so a trained
/// road model can be added without rewriting navigation state management.
/// </summary>
public interface IRoadSceneAnalyzer
{
    bool IsModelReady { get; }
    string Status { get; }

    Task<RoadSceneObservation> AnalyzeAsync(
        Bitmap roadView,
        CancellationToken cancellationToken = default);
}

public sealed class LocalRoadSceneAnalyzer : IRoadSceneAnalyzer
{
    public bool IsModelReady => false;

    public string Status =>
        "深度道路模型接口已就绪；当前未安装本地 ONNX 道路模型";

    public Task<RoadSceneObservation> AnalyzeAsync(
        Bitmap roadView,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // v0.11 deliberately does not pretend that a color heuristic is a
        // trained deep-learning model. Motion/dead-reckoning is active now;
        // semantic road/depth inference will activate only when a validated
        // local model is shipped.
        return Task.FromResult(
            new RoadSceneObservation
            {
                ModelReady = false,
                Confidence = 0,
                Summary = Status
            });
    }
}

public sealed class RoadSceneObservation
{
    public bool ModelReady { get; set; }
    public double Confidence { get; set; }
    public double Drivable01 { get; set; }
    public double Obstacle01 { get; set; }
    public double RoughRoad01 { get; set; }
    public string Summary { get; set; } = "";
}

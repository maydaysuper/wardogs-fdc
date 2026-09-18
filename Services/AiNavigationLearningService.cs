using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class AiNavigationLearningService
{
    private readonly DeepSeekClient _deepSeek;

    public AiNavigationLearningService(DeepSeekClient deepSeek)
    {
        _deepSeek = deepSeek;
    }

    public async Task<AiNavigationLearningReport> AnalyzeAsync(
        string apiKey,
        string model,
        string mapId,
        RoadGraph graph,
        NavigationLearningSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var usefulRecent = snapshot.Recent
            .Where(x => x.EdgeObservations.Count > 0)
            .Take(30)
            .ToList();

        if (usefulRecent.Count < 2)
        {
            return new AiNavigationLearningReport
            {
                Summary =
                    "已有导航经验，但带具体 Road Graph 路段观测的样本不足 2 次；" +
                    "继续使用实时导航后再进行 AI 学习。",
                Suggestions = new List<AiRoadSuggestion>()
            };
        }

        var aggregate = usefulRecent
            .SelectMany(exp =>
                exp.EdgeObservations.Select(obs => new
                {
                    exp.VehicleId,
                    exp.Completed,
                    exp.Replans,
                    exp.MaxDeviationMeters,
                    Observation = obs
                }))
            .GroupBy(
                x => new
                {
                    x.Observation.EdgeId,
                    x.VehicleId
                })
            .Select(g =>
            {
                var distanceKm = g.Sum(x => x.Observation.DistanceKm);
                var seconds = g.Sum(x => x.Observation.Seconds);

                return new
                {
                    edgeId = g.Key.EdgeId,
                    vehicleId = g.Key.VehicleId,
                    trips = g.Count(),
                    samples = g.Sum(x => x.Observation.Samples),
                    distanceKm,
                    seconds,
                    observedSpeedKmh =
                        seconds > 0.5
                            ? distanceKm / (seconds / 3600.0)
                            : 0,
                    maxDeviationMeters =
                        g.Max(x => x.Observation.MaxDeviationMeters),
                    completedTrips =
                        g.Count(x => x.Completed),
                    averageReplans =
                        g.Average(x => x.Replans)
                };
            })
            .Where(x => x.samples >= 2)
            .OrderByDescending(x => x.samples)
            .Take(120)
            .ToList();

        if (aggregate.Count == 0)
        {
            return new AiNavigationLearningReport
            {
                Summary = "路段样本尚不足以形成稳定的车型/道路观测。",
                Suggestions = new List<AiRoadSuggestion>()
            };
        }

        var relevantEdgeIds = aggregate
            .Select(x => x.edgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var edgeData = graph.Edges
            .Where(e => relevantEdgeIds.Contains(e.Id))
            .Take(160)
            .Select(e => new
            {
                e.Id,
                RoadClass = e.Class.ToString(),
                e.Source,
                e.Verified,
                e.Risk,
                e.Traversals,
                e.AutoScore,
                e.VehicleSpeedMultipliers,
                e.AiConfidence
            })
            .ToList();

        var tripSummary = usefulRecent
            .Select(x => new
            {
                x.VehicleId,
                x.Completed,
                x.PlannedDistanceKm,
                x.ActualDistanceKm,
                x.DurationMinutes,
                x.Replans,
                x.MaxDeviationMeters
            })
            .ToList();

        var input = new
        {
            mapId,
            snapshot.ExperienceCount,
            snapshot.CompletedCount,
            snapshot.AverageReplans,
            snapshot.AverageMaxDeviationMeters,
            trips = tripSummary,
            edgeObservations = aggregate,
            edges = edgeData
        };

        var system =
            "你是 WARDOGS 导航学习分析器。只分析本地程序已经收集到的路线经验，" +
            "不能编造地图、道路、敌情、速度或坐标。输出必须是 JSON。 " +
            "每条建议必须引用输入中真实存在的 edgeId 和 vehicleId。 " +
            "riskDelta 范围 -0.20 到 0.20；speedMultiplier 范围 0.55 到 1.25；" +
            "confidence 范围 0 到 1。 " +
            "speedMultiplier 是对该车型在该路段相对既有道路速度模型的修正，不是绝对速度。 " +
            "只有多个样本、足够采样点或持续异常时才给高置信度；单次异常必须低置信。 " +
            "不要把普通偏航自动解释为敌情或危险。 " +
            "JSON 格式示例：{\"summary\":\"...\",\"suggestions\":[{" +
            "\"edgeId\":\"e1\",\"vehicleId\":\"ural\",\"riskDelta\":0.05," +
            "\"speedMultiplier\":0.82,\"confidence\":0.78,\"reason\":\"...\"}]}";

        var user =
            "请根据以下导航经验 JSON 生成路段学习建议。 " +
            "优先校准车型速度修正；只有数据明确显示持续绕行/高偏差时才建议调整风险。" +
            "\n" +
            JsonSerializer.Serialize(input);

        var report = await _deepSeek.AskJsonAsync<AiNavigationLearningReport>(
            apiKey,
            model,
            system,
            user,
            cancellationToken);

        report.Suggestions ??= new List<AiRoadSuggestion>();

        var validEdges = graph.Edges
            .Select(e => e.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var validVehicles = aggregate
            .Select(x => x.vehicleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        report.Suggestions = report.Suggestions
            .Where(s =>
                validEdges.Contains(s.EdgeId) &&
                validVehicles.Contains(s.VehicleId))
            .Select(s => new AiRoadSuggestion
            {
                EdgeId = s.EdgeId,
                VehicleId = s.VehicleId,
                RiskDelta = Math.Clamp(s.RiskDelta, -0.20, 0.20),
                SpeedMultiplier = Math.Clamp(s.SpeedMultiplier, 0.55, 1.25),
                Confidence = Math.Clamp(s.Confidence, 0, 1),
                Reason = s.Reason ?? ""
            })
            .ToList();

        return report;
    }
}

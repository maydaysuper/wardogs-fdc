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
        var relevantEdgeIds = snapshot.Recent
            .SelectMany(x => x.EdgeIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(120)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var edgeData = graph.Edges
            .Where(e => relevantEdgeIds.Count == 0 || relevantEdgeIds.Contains(e.Id))
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

        var input = new
        {
            mapId,
            experienceCount = snapshot.ExperienceCount,
            completedCount = snapshot.CompletedCount,
            averageReplans = snapshot.AverageReplans,
            averageMaxDeviationMeters = snapshot.AverageMaxDeviationMeters,
            recent = snapshot.Recent.Take(30),
            edges = edgeData
        };

        var system =
            "你是 WARDOGS 导航学习分析器。只分析本地程序已经收集到的路线经验，" +
            "不能编造地图、道路、敌情、速度或坐标。输出必须是 JSON。 " +
            "每条建议必须引用输入中真实存在的 edgeId。 " +
            "riskDelta 范围 -0.20 到 0.20；speedMultiplier 范围 0.55 到 1.25；" +
            "confidence 范围 0 到 1。样本不足时应返回较低 confidence，甚至空 suggestions。 " +
            "JSON 格式示例：{\"summary\":\"...\",\"suggestions\":[{" +
            "\"edgeId\":\"e1\",\"vehicleId\":\"ural\",\"riskDelta\":0.05," +
            "\"speedMultiplier\":0.82,\"confidence\":0.78,\"reason\":\"...\"}]}";

        var user =
            "请根据以下导航经验 JSON 生成路段学习建议。不要仅因为一次偏航就判定道路危险。" +
            "只有多个样本或明显持续异常时才给高置信建议。\n" +
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

        report.Suggestions = report.Suggestions
            .Where(s => validEdges.Contains(s.EdgeId))
            .Select(s => new AiRoadSuggestion
            {
                EdgeId = s.EdgeId,
                VehicleId = string.IsNullOrWhiteSpace(s.VehicleId) ? "generic-ground" : s.VehicleId,
                RiskDelta = Math.Clamp(s.RiskDelta, -0.20, 0.20),
                SpeedMultiplier = Math.Clamp(s.SpeedMultiplier, 0.55, 1.25),
                Confidence = Math.Clamp(s.Confidence, 0, 1),
                Reason = s.Reason ?? ""
            })
            .ToList();

        return report;
    }
}

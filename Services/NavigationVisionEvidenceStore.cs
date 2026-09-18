using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class NavigationVisionEvidenceStore
{
    private readonly string _path;
    private readonly object _sync = new();

    public NavigationVisionEvidenceStore(string? root = null)
    {
        var dir = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "vision-edge-evidence.json");
    }

    public List<VisionEdgeEvidence> GetActive(string mapId)
    {
        lock (_sync)
        {
            var all = LoadAll();
            var now = DateTime.UtcNow;
            var removed = all.RemoveAll(x => x.ExpiresUtc <= now);

            if (removed > 0)
                SaveAll(all);

            return all
                .Where(x =>
                    x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
                    x.ExpiresUtc > now)
                .ToList();
        }
    }

    public Dictionary<string, double> GetRiskMap(string mapId)
    {
        return GetActive(mapId)
            .GroupBy(x => x.EdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Max(x =>
                    Math.Clamp(x.Severity, 0, 1) *
                    Math.Clamp(x.Confidence, 0, 1)),
                StringComparer.OrdinalIgnoreCase);
    }

    public int ApplyReport(
        string mapId,
        AiVisionNavigationReport report,
        IReadOnlySet<string> validEdgeIds,
        double minimumConfidence = 0.80,
        TimeSpan? ttl = null)
    {
        var lifetime = ttl ?? TimeSpan.FromMinutes(8);
        var accepted = report.Findings
            .Where(x =>
                validEdgeIds.Contains(x.EdgeId) &&
                x.Confidence >= minimumConfidence &&
                (
                    x.Kind.Equals(
                        "blocked",
                        StringComparison.OrdinalIgnoreCase) ||
                    x.Kind.Equals(
                        "danger",
                        StringComparison.OrdinalIgnoreCase)
                ))
            .Select(x => new VisionEdgeEvidence
            {
                MapId = mapId,
                EdgeId = x.EdgeId,
                Kind = NormalizeKind(x.Kind),
                Severity = Math.Clamp(x.Severity, 0, 1),
                Confidence = Math.Clamp(x.Confidence, 0, 1),
                Reason = x.Reason ?? "",
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.Add(lifetime)
            })
            .ToList();

        lock (_sync)
        {
            var all = LoadAll();
            all.RemoveAll(x =>
                x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
                accepted.Any(a =>
                    a.EdgeId.Equals(x.EdgeId, StringComparison.OrdinalIgnoreCase)));

            all.AddRange(accepted);
            SaveAll(all);
        }

        return accepted.Count;
    }

    public int ClearMap(string mapId)
    {
        lock (_sync)
        {
            var all = LoadAll();
            var removed = all.RemoveAll(x =>
                x.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
            SaveAll(all);
            return removed;
        }
    }

    private static string NormalizeKind(string? kind) =>
        (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "blocked" => "blocked",
            "danger" => "danger",
            "clear" => "clear",
            _ => "uncertain"
        };

    private List<VisionEdgeEvidence> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return new();

            return JsonSerializer.Deserialize<List<VisionEdgeEvidence>>(
                File.ReadAllText(_path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
                ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveAll(List<VisionEdgeEvidence> items)
    {
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(
                items,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}

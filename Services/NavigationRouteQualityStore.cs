using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class NavigationRouteQualityStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public NavigationRouteQualityStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "route-quality.json");
    }

    public RouteQualityAssessment Assess(
        string mapId,
        string vehicleId,
        RoutePreference preference,
        RoutePlan route)
    {
        var key = BuildKey(mapId, vehicleId, preference, route.EdgeIds);

        lock (_sync)
        {
            var all = Load();
            if (!all.TryGetValue(key, out var profile))
                return new RouteQualityAssessment();

            return new RouteQualityAssessment
            {
                Samples = profile.Samples,
                Reliability = profile.Reliability,
                EtaMultiplier = Math.Clamp(profile.EtaRatioEwma, 0.72, 1.65),
                ExpectedReplans = Math.Max(0, profile.ReplansEwma),
                ExpectedMaxDeviationMeters = Math.Max(0, profile.MaxDeviationEwma)
            };
        }
    }

    public void Record(NavigationExperience experience)
    {
        if (experience.EdgeIds.Count == 0 ||
            experience.PlannedDistanceKm <= 0.01)
            return;

        var key = BuildKey(
            experience.MapId,
            experience.VehicleId,
            experience.Preference,
            experience.EdgeIds);

        lock (_sync)
        {
            var all = Load();

            if (!all.TryGetValue(key, out var profile))
            {
                profile = new RouteQualityProfile
                {
                    Key = key,
                    MapId = experience.MapId,
                    VehicleId = experience.VehicleId,
                    Preference = experience.Preference,
                    RouteSignature = BuildSignature(experience.EdgeIds)
                };
                all[key] = profile;
            }

            var duration = Math.Max(0.05, experience.DurationMinutes);
            var expectedMinutes = experience.VehicleBaseSpeedKmh > 1
                ? experience.PlannedDistanceKm / experience.VehicleBaseSpeedKmh * 60.0
                : duration;

            var etaRatio = Math.Clamp(
                duration / Math.Max(0.05, expectedMinutes),
                0.55,
                2.20);

            var alpha = profile.Samples switch
            {
                <= 0 => 1.0,
                < 3 => 0.42,
                < 8 => 0.28,
                _ => 0.16
            };

            profile.Samples++;
            if (experience.Completed)
                profile.CompletedSamples++;

            profile.EtaRatioEwma = Blend(profile.EtaRatioEwma, etaRatio, alpha);
            profile.ReplansEwma = Blend(profile.ReplansEwma, experience.Replans, alpha);
            profile.MaxDeviationEwma = Blend(profile.MaxDeviationEwma, experience.MaxDeviationMeters, alpha);

            var completionRate = profile.Samples <= 0
                ? 0
                : profile.CompletedSamples / (double)profile.Samples;

            var replanPenalty = Math.Clamp(profile.ReplansEwma / 3.0, 0, 0.65);
            var deviationPenalty = Math.Clamp(profile.MaxDeviationEwma / 220.0, 0, 0.65);
            var sampleConfidence = 1.0 - Math.Exp(-profile.Samples / 4.0);

            var rawReliability = Math.Clamp(
                completionRate * 0.55 +
                (1.0 - replanPenalty) * 0.25 +
                (1.0 - deviationPenalty) * 0.20,
                0,
                1);

            profile.Reliability =
                0.50 * (1.0 - sampleConfidence) +
                rawReliability * sampleConfidence;

            profile.UpdatedUtc = DateTime.UtcNow;
            Save(all);
        }
    }

    public int ClearMap(string mapId)
    {
        lock (_sync)
        {
            var all = Load();
            var keys = all
                .Where(x => x.Value.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Key)
                .ToList();

            foreach (var key in keys)
                all.Remove(key);

            if (keys.Count > 0)
                Save(all);

            return keys.Count;
        }
    }

    public static string BuildSignature(IEnumerable<string> edgeIds)
    {
        var ids = edgeIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();

        if (ids.Length == 0)
            return "no-edges";

        unchecked
        {
            ulong hash = 1469598103934665603UL;
            foreach (var id in ids)
            {
                foreach (var ch in id.ToUpperInvariant())
                {
                    hash ^= ch;
                    hash *= 1099511628211UL;
                }
                hash ^= (byte)'|';
                hash *= 1099511628211UL;
            }
            return hash.ToString("X16");
        }
    }

    public static string BuildKey(
        string mapId,
        string vehicleId,
        RoutePreference preference,
        IEnumerable<string> edgeIds) =>
        mapId.ToLowerInvariant() + "|" +
        vehicleId.ToLowerInvariant() + "|" +
        preference + "|" +
        BuildSignature(edgeIds);

    private static double Blend(double oldValue, double newValue, double alpha)
    {
        if (!double.IsFinite(oldValue)) oldValue = newValue;
        return oldValue * (1.0 - alpha) + newValue * alpha;
    }

    private Dictionary<string, RouteQualityProfile> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return NewDictionary();

            var parsed = JsonSerializer.Deserialize<Dictionary<string, RouteQualityProfile>>(
                File.ReadAllText(_path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return parsed == null
                ? NewDictionary()
                : new Dictionary<string, RouteQualityProfile>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return NewDictionary();
        }
    }

    private void Save(Dictionary<string, RouteQualityProfile> all)
    {
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Dictionary<string, RouteQualityProfile> NewDictionary() =>
        new(StringComparer.OrdinalIgnoreCase);
}

using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class NavigationExperienceStore
{
    private readonly string _root;
    private readonly object _sync = new();

    public NavigationExperienceStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator",
            "learning");
        Directory.CreateDirectory(_root);
    }

    public void Append(NavigationExperience experience)
    {
        lock (_sync)
        {
            var list = Load(experience.MapId);
            list.Add(experience);

            // Bound local history to avoid unbounded growth.
            if (list.Count > 500)
                list = list
                    .OrderByDescending(x => x.EndedUtc)
                    .Take(500)
                    .OrderBy(x => x.EndedUtc)
                    .ToList();

            Save(experience.MapId, list);
        }
    }

    public List<NavigationExperience> Load(string mapId)
    {
        lock (_sync)
        {
            try
            {
                var path = PathFor(mapId);
                if (!File.Exists(path)) return new();

                return JsonSerializer.Deserialize<List<NavigationExperience>>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new();
            }
            catch
            {
                return new();
            }
        }
    }

    public NavigationLearningSnapshot Snapshot(string mapId, int recentCount = 30)
    {
        var list = Load(mapId);
        var recent = list
            .OrderByDescending(x => x.EndedUtc)
            .Take(recentCount)
            .ToList();

        return new NavigationLearningSnapshot
        {
            MapId = mapId,
            ExperienceCount = list.Count,
            CompletedCount = list.Count(x => x.Completed),
            AverageReplans = recent.Count == 0 ? 0 : recent.Average(x => x.Replans),
            AverageMaxDeviationMeters = recent.Count == 0 ? 0 : recent.Average(x => x.MaxDeviationMeters),
            Recent = recent
        };
    }

    private void Save(string mapId, List<NavigationExperience> list)
    {
        File.WriteAllText(
            PathFor(mapId),
            JsonSerializer.Serialize(
                list,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private string PathFor(string mapId)
    {
        var safe = new string(
            mapId.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return Path.Combine(_root, safe + ".experiences.json");
    }
}

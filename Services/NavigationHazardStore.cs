using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class NavigationHazardStore
{
    private readonly string _path;
    private readonly object _sync = new();

    public NavigationHazardStore(string? root = null)
    {
        var dir = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "navigation-hazards.json");
    }

    public List<NavigationHazard> GetActive(string mapId)
    {
        lock (_sync)
        {
            var all = LoadAll();
            var now = DateTime.UtcNow;
            var active = all
                .Where(h =>
                    h.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase) &&
                    h.ExpiresUtc > now)
                .ToList();

            if (all.RemoveAll(h => h.ExpiresUtc <= now) > 0)
                SaveAll(all);

            return active;
        }
    }

    public NavigationHazard Add(
        string mapId,
        MapPoint center,
        double radiusMeters,
        double severity,
        TimeSpan ttl,
        string label = "临时危险区",
        string source = "manual")
    {
        var hazard = new NavigationHazard
        {
            Id = Guid.NewGuid().ToString("N"),
            MapId = mapId,
            Center = center,
            RadiusMeters = Math.Clamp(radiusMeters, 50, 3000),
            Severity = Math.Clamp(severity, 0, 1),
            ExpiresUtc = DateTime.UtcNow.Add(ttl),
            Label = label,
            Source = source
        };

        lock (_sync)
        {
            var all = LoadAll();
            all.Add(hazard);
            SaveAll(all);
        }

        return hazard;
    }

    public int ClearMap(string mapId)
    {
        lock (_sync)
        {
            var all = LoadAll();
            var removed = all.RemoveAll(h =>
                h.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
            SaveAll(all);
            return removed;
        }
    }

    private List<NavigationHazard> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<List<NavigationHazard>>(
                File.ReadAllText(_path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveAll(List<NavigationHazard> hazards)
    {
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(
                hazards,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}

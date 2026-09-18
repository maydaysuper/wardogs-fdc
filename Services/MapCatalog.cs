using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class MapCatalog
{
    private readonly Dictionary<string, MapDefinition> _maps;

    public MapCatalog(string? dataRoot = null)
    {
        var root = dataRoot ?? Path.Combine(AppContext.BaseDirectory, "Data");
        var path = Path.Combine(root, "maps.json");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _maps = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, MapDefinition>>(File.ReadAllText(path), opts)
                ?? new Dictionary<string, MapDefinition>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MapDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> MapIds => _maps.Keys;

    public MapDefinition? Get(string id) =>
        _maps.TryGetValue(id, out var map) ? map : _maps.Values.FirstOrDefault();

    public List<Destination> GetEconomicDestinations(string id)
    {
        var map = Get(id);
        if (map == null) return new List<Destination>();

        var towers = map.Markers
            .Where(m => m.Kind.Equals("tower", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = towers.Select((t, i) => new Destination
        {
            Id = "tower-" + (i + 1),
            Label = t.Label,
            Kind = DestinationKind.Zone,
            Position = t.Position
        }).ToList();

        if (towers.Count > 0)
        {
            var center = new MapPoint(towers.Average(t => t.Position.X), towers.Average(t => t.Position.Y));
            result.Insert(0, new Destination
            {
                Id = "front-fob",
                Label = "前线 FOB",
                Kind = DestinationKind.Fob,
                Position = center
            });
        }
        return result;
    }
}

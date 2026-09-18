using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class NavigationMapMemoryStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public NavigationMapMemoryStore(
        string? directory = null)
    {
        var dir =
            directory ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WardogsNavigator");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(
            dir,
            "visual-map-memory.json");
    }

    public MapVisualMemory Get(string mapId)
    {
        lock (_sync)
        {
            var all = Load();
            if (all.TryGetValue(
                    mapId,
                    out var memory))
                return Clone(memory);

            return new MapVisualMemory
            {
                MapId = mapId
            };
        }
    }

    public void RecordRegistration(
        string mapId,
        MapViewportRegistration registration)
    {
        lock (_sync)
        {
            var all = Load();
            var memory = GetOrCreate(
                all,
                mapId);

            memory.LastRegistration =
                Clone(registration);

            memory.SuccessfulRegistrations++;

            memory.RegistrationConfidenceEwma =
                memory.SuccessfulRegistrations <= 1
                    ? registration.Confidence
                    : memory.RegistrationConfidenceEwma *
                      0.82 +
                      registration.Confidence *
                      0.18;

            memory.UpdatedUtc =
                DateTime.UtcNow;

            Save(all);
        }
    }

    public void RecordRegistrationFailure(
        string mapId)
    {
        lock (_sync)
        {
            var all = Load();
            var memory = GetOrCreate(
                all,
                mapId);

            memory.FailedRegistrations++;
            memory.RegistrationConfidenceEwma *=
                0.94;
            memory.UpdatedUtc =
                DateTime.UtcNow;

            Save(all);
        }
    }

    public void RecordTarget(
        string mapId,
        MapPoint point,
        double confidence)
    {
        lock (_sync)
        {
            var all = Load();
            var memory = GetOrCreate(
                all,
                mapId);

            memory.LastVisualTarget =
                point;
            memory.LastTargetConfidence =
                Math.Clamp(
                    confidence,
                    0,
                    1);
            memory.UpdatedUtc =
                DateTime.UtcNow;

            Save(all);
        }
    }

    public int Clear(string mapId)
    {
        lock (_sync)
        {
            var all = Load();
            if (!all.Remove(mapId))
                return 0;

            Save(all);
            return 1;
        }
    }

    private Dictionary<string, MapVisualMemory> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return NewDictionary();

            var parsed =
                JsonSerializer.Deserialize<
                    Dictionary<string, MapVisualMemory>>(
                    File.ReadAllText(_path),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    });

            return parsed ??
                   NewDictionary();
        }
        catch
        {
            return NewDictionary();
        }
    }

    private void Save(
        Dictionary<string, MapVisualMemory> all)
    {
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(
                all,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    private static Dictionary<string, MapVisualMemory>
        NewDictionary() =>
        new(
            StringComparer.OrdinalIgnoreCase);

    private static MapVisualMemory GetOrCreate(
        Dictionary<string, MapVisualMemory> all,
        string mapId)
    {
        if (all.TryGetValue(
                mapId,
                out var existing))
            return existing;

        var memory =
            new MapVisualMemory
            {
                MapId = mapId
            };

        all[mapId] = memory;
        return memory;
    }

    private static T Clone<T>(T value)
    {
        var json =
            JsonSerializer.Serialize(value);

        return JsonSerializer.Deserialize<T>(json)!;
    }
}

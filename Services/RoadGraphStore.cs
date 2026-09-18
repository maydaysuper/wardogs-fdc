using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class RoadGraphStore
{
    private readonly string _root;
    private readonly Dictionary<string, RoadGraph> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public RoadGraphStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator",
            "roadgraphs");
        Directory.CreateDirectory(_root);
    }

    public string RootDirectory => _root;

    public RoadGraph Load(string mapId)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(mapId, out var cached))
                return Clone(cached);

            var graph = LoadFromDisk(mapId);
            _cache[mapId] = graph;
            return Clone(graph);
        }
    }

    public void Save(RoadGraph graph)
    {
        graph.UpdatedUtc = DateTime.UtcNow;
        Normalize(graph);

        lock (_sync)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(
                PathFor(graph.MapId),
                JsonSerializer.Serialize(graph, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            _cache[graph.MapId] = Clone(graph);
        }
    }

    public RoadGraph Reset(string mapId)
    {
        var graph = new RoadGraph { MapId = mapId };
        Save(graph);
        return graph;
    }

    public RoadGraphStats GetStats(RoadGraph graph)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        double meters = 0;

        foreach (var edge in graph.Edges)
        {
            if (!nodes.TryGetValue(edge.A, out var a) ||
                !nodes.TryGetValue(edge.B, out var b))
                continue;

            meters += a.Position.DistanceMeters(b.Position);
        }

        return new RoadGraphStats
        {
            Nodes = graph.Nodes.Count,
            Edges = graph.Edges.Count,
            VerifiedEdges = graph.Edges.Count(e => e.Verified && !e.Blocked),
            LearnedEdges = graph.Edges.Count(e => e.Source.Equals("trace", StringComparison.OrdinalIgnoreCase)),
            NetworkKm = meters / 1000.0
        };
    }

    public void AddManualPoint(RoadGraph graph, MapPoint point, ref string? previousNodeId, RoadClass roadClass)
    {
        var node = FindOrCreateNode(graph, point, 18.0);
        if (previousNodeId != null && !previousNodeId.Equals(node.Id, StringComparison.OrdinalIgnoreCase))
            AddOrTouchEdge(graph, previousNodeId, node.Id, roadClass, "manual", true);

        previousNodeId = node.Id;
        graph.UpdatedUtc = DateTime.UtcNow;
    }

    public void MergeTrace(RoadGraph graph, IReadOnlyList<MapPoint> trace, RoadClass roadClass = RoadClass.Secondary)
    {
        if (trace.Count < 2) return;

        string? previous = null;
        foreach (var point in trace)
        {
            var node = FindOrCreateNode(graph, point, 24.0);
            if (previous != null && !previous.Equals(node.Id, StringComparison.OrdinalIgnoreCase))
                AddOrTouchEdge(graph, previous, node.Id, roadClass, "trace", true, incrementTraversal: true);
            previous = node.Id;
        }

        graph.UpdatedUtc = DateTime.UtcNow;
    }

    public bool RemoveLastManualSegment(RoadGraph graph, string? currentNodeId, out string? previousNodeId)
    {
        previousNodeId = currentNodeId;
        if (string.IsNullOrWhiteSpace(currentNodeId)) return false;

        var candidate = graph.Edges
            .LastOrDefault(e =>
                e.Source.Equals("manual", StringComparison.OrdinalIgnoreCase) &&
                (e.A.Equals(currentNodeId, StringComparison.OrdinalIgnoreCase) ||
                 e.B.Equals(currentNodeId, StringComparison.OrdinalIgnoreCase)));

        if (candidate == null) return false;

        previousNodeId = candidate.A.Equals(currentNodeId, StringComparison.OrdinalIgnoreCase)
            ? candidate.B
            : candidate.A;

        graph.Edges.Remove(candidate);
        PruneUnusedNodes(graph);

        if (previousNodeId != null &&
            !graph.Nodes.Any(n => n.Id.Equals(previousNodeId, StringComparison.OrdinalIgnoreCase)))
            previousNodeId = null;

        graph.UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public static RoadNode FindOrCreateNode(RoadGraph graph, MapPoint point, double mergeMeters)
    {
        var nearest = graph.Nodes
            .Select(n => new { Node = n, Distance = n.Position.DistanceMeters(point) })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        if (nearest != null && nearest.Distance <= mergeMeters)
            return nearest.Node;

        var node = new RoadNode
        {
            Id = "n" + Guid.NewGuid().ToString("N")[..10],
            Position = point
        };
        graph.Nodes.Add(node);
        return node;
    }

    public static void AddOrTouchEdge(
        RoadGraph graph,
        string a,
        string b,
        RoadClass roadClass,
        string source,
        bool verified,
        bool incrementTraversal = false)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return;

        var existing = graph.Edges.FirstOrDefault(e =>
            (e.A.Equals(a, StringComparison.OrdinalIgnoreCase) &&
             e.B.Equals(b, StringComparison.OrdinalIgnoreCase)) ||
            (e.A.Equals(b, StringComparison.OrdinalIgnoreCase) &&
             e.B.Equals(a, StringComparison.OrdinalIgnoreCase)));

        if (existing != null)
        {
            existing.Verified |= verified;
            if (incrementTraversal) existing.Traversals++;
            if (source.Equals("manual", StringComparison.OrdinalIgnoreCase))
                existing.Source = "manual";
            return;
        }

        graph.Edges.Add(new RoadEdge
        {
            Id = "e" + Guid.NewGuid().ToString("N")[..10],
            A = a,
            B = b,
            Class = roadClass,
            Source = source,
            Verified = verified,
            Traversals = incrementTraversal ? 1 : 0
        });
    }

    private RoadGraph LoadFromDisk(string mapId)
    {
        try
        {
            var path = PathFor(mapId);
            if (!File.Exists(path))
                return new RoadGraph { MapId = mapId };

            var graph = JsonSerializer.Deserialize<RoadGraph>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new RoadGraph { MapId = mapId };

            graph.MapId = mapId;
            Normalize(graph);
            return graph;
        }
        catch
        {
            return new RoadGraph { MapId = mapId };
        }
    }

    private string PathFor(string mapId) =>
        Path.Combine(_root, Sanitize(mapId) + ".json");

    private static string Sanitize(string mapId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(mapId.Where(c => !invalid.Contains(c)).ToArray());
    }

    private static void Normalize(RoadGraph graph)
    {
        graph.Nodes ??= new List<RoadNode>();
        graph.Edges ??= new List<RoadEdge>();

        var nodeIds = graph.Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        graph.Edges = graph.Edges
            .Where(e =>
                !string.IsNullOrWhiteSpace(e.Id) &&
                nodeIds.Contains(e.A) &&
                nodeIds.Contains(e.B) &&
                !e.A.Equals(e.B, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void PruneUnusedNodes(RoadGraph graph)
    {
        var used = graph.Edges
            .SelectMany(e => new[] { e.A, e.B })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        graph.Nodes.RemoveAll(n => !used.Contains(n.Id));
    }

    private static RoadGraph Clone(RoadGraph graph)
    {
        var json = JsonSerializer.Serialize(graph);
        return JsonSerializer.Deserialize<RoadGraph>(json) ?? new RoadGraph { MapId = graph.MapId };
    }
}

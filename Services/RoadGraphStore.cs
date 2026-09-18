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
            AutoEdges = graph.Edges.Count(e => e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase)),
            AiLearnedEdges = graph.Edges.Count(e =>
                e.AiConfidence > 0 ||
                Math.Abs(e.AiRiskAdjustment) > 1e-9 ||
                (e.VehicleSpeedMultipliers?.Count ?? 0) > 0),
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


    public void ReplaceAutoGraph(RoadGraph graph, RoadGraph autoGraph)
    {
        // Remove only previous automatic edges. Manual and driven/trace edges are preserved.
        graph.Edges.RemoveAll(e =>
            e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase));

        PruneUnusedNodes(graph);

        var nodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var autoNode in autoGraph.Nodes)
        {
            var target = FindOrCreateNode(graph, autoNode.Position, 34.0);
            nodeMap[autoNode.Id] = target.Id;
        }

        foreach (var autoEdge in autoGraph.Edges)
        {
            if (!nodeMap.TryGetValue(autoEdge.A, out var a) ||
                !nodeMap.TryGetValue(autoEdge.B, out var b) ||
                a.Equals(b, StringComparison.OrdinalIgnoreCase))
                continue;

            var existing = graph.Edges.FirstOrDefault(e =>
                (e.A.Equals(a, StringComparison.OrdinalIgnoreCase) &&
                 e.B.Equals(b, StringComparison.OrdinalIgnoreCase)) ||
                (e.A.Equals(b, StringComparison.OrdinalIgnoreCase) &&
                 e.B.Equals(a, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                // Never downgrade manual/trace verification with automatic data.
                if (existing.Source.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    existing.Class = autoEdge.Class;
                    existing.AutoScore = Math.Max(existing.AutoScore, autoEdge.AutoScore);
                    existing.Verified = false;
                }

                continue;
            }

            graph.Edges.Add(new RoadEdge
            {
                Id = "auto_e_" + Guid.NewGuid().ToString("N")[..10],
                A = a,
                B = b,
                Class = autoEdge.Class,
                Blocked = false,
                Verified = false,
                Risk = 0,
                Traversals = 0,
                AutoScore = autoEdge.AutoScore,
                Source = "auto"
            });
        }

        graph.Version = Math.Max(graph.Version, 3);
        graph.UpdatedUtc = DateTime.UtcNow;
    }


    public int RecordNavigationExperience(
        RoadGraph graph,
        NavigationExperience experience)
    {
        var changed = 0;

        foreach (var observation in experience.EdgeObservations)
        {
            if (observation.Samples < 2 ||
                observation.DistanceKm < 0.015 ||
                observation.MaxDeviationMeters > 140)
                continue;

            var edge = graph.Edges.FirstOrDefault(e =>
                e.Id.Equals(
                    observation.EdgeId,
                    StringComparison.OrdinalIgnoreCase));

            if (edge == null)
                continue;

            edge.Traversals++;
            changed++;

            edge.LocalVehicleSpeedMultipliers ??=
                new Dictionary<string, double>();

            if (observation.Samples >= 3 &&
                observation.DistanceKm >= 0.03 &&
                observation.Seconds >= 2 &&
                observation.ObservedSpeedKmh > 2)
            {
                var profile =
                    VehicleRoutingProfileService.ForVehicleId(
                        experience.VehicleId);

                var expectedSpeed =
                    Math.Max(
                        5,
                        experience.VehicleBaseSpeedKmh *
                        profile.FactorFor(edge.Class));

                var observedRatio = Math.Clamp(
                    observation.ObservedSpeedKmh /
                    expectedSpeed,
                    0.55,
                    1.25);

                if (edge.LocalVehicleSpeedMultipliers.TryGetValue(
                        experience.VehicleId,
                        out var existing))
                {
                    // EWMA: preserve history while still adapting to new road evidence.
                    edge.LocalVehicleSpeedMultipliers[
                        experience.VehicleId] =
                        Math.Clamp(
                            existing * 0.72 +
                            observedRatio * 0.28,
                            0.55,
                            1.25);
                }
                else
                {
                    edge.LocalVehicleSpeedMultipliers[
                        experience.VehicleId] =
                        observedRatio;
                }
            }

            // Strong real-driving evidence can promote an automatic guess.
            if (edge.Source.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                observation.Samples >= 3 &&
                observation.DistanceKm >= 0.03 &&
                observation.MaxDeviationMeters <= 90)
            {
                edge.Source = "trace";
                edge.Verified = true;
                edge.AutoScore = 0;
            }
        }

        if (changed > 0)
            graph.UpdatedUtc = DateTime.UtcNow;

        return changed;
    }

    public int ApplyAiSuggestions(
        RoadGraph graph,
        IEnumerable<AiRoadSuggestion> suggestions,
        double minimumConfidence = 0.72)
    {
        var applied = 0;

        foreach (var suggestion in suggestions)
        {
            if (suggestion.Confidence < minimumConfidence)
                continue;

            var edge = graph.Edges.FirstOrDefault(e =>
                e.Id.Equals(suggestion.EdgeId, StringComparison.OrdinalIgnoreCase));

            if (edge == null)
                continue;

            edge.AiRiskAdjustment = Math.Clamp(
                suggestion.RiskDelta,
                -0.20,
                0.20);

            edge.VehicleSpeedMultipliers ??= new Dictionary<string, double>();
            edge.VehicleSpeedMultipliers[suggestion.VehicleId] =
                Math.Clamp(suggestion.SpeedMultiplier, 0.55, 1.25);

            edge.AiConfidence = Math.Clamp(suggestion.Confidence, 0, 1);
            edge.AiNote = suggestion.Reason ?? "";
            edge.AiUpdatedUtc = DateTime.UtcNow;
            applied++;
        }

        if (applied > 0)
            graph.UpdatedUtc = DateTime.UtcNow;

        return applied;
    }

    public int RemoveAiLearning(RoadGraph graph)
    {
        var changed = 0;

        foreach (var edge in graph.Edges)
        {
            if ((edge.VehicleSpeedMultipliers?.Count ?? 0) == 0 &&
                Math.Abs(edge.AiRiskAdjustment) <= 1e-9 &&
                edge.AiConfidence <= 0 &&
                string.IsNullOrWhiteSpace(edge.AiNote))
                continue;

            edge.VehicleSpeedMultipliers = new Dictionary<string, double>();
            edge.AiRiskAdjustment = 0;
            edge.AiConfidence = 0;
            edge.AiNote = "";
            edge.AiUpdatedUtc = null;
            changed++;
        }

        if (changed > 0)
            graph.UpdatedUtc = DateTime.UtcNow;

        return changed;
    }

    public int RemoveAutoGraph(RoadGraph graph)
    {
        var removed = graph.Edges.RemoveAll(e =>
            e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase));

        PruneUnusedNodes(graph);
        graph.UpdatedUtc = DateTime.UtcNow;
        return removed;
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

        var previousCandidate = previousNodeId;
        if (previousCandidate != null &&
            !graph.Nodes.Any(n => n.Id.Equals(previousCandidate, StringComparison.OrdinalIgnoreCase)))
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
            {
                existing.Source = "manual";
            }
            else if (source.Equals("trace", StringComparison.OrdinalIgnoreCase) &&
                     existing.Source.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                // A real driven trace upgrades an automatic guess into verified road data.
                existing.Source = "trace";
                existing.AutoScore = 0;
            }

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

        foreach (var edge in graph.Edges)
        {
            edge.LocalVehicleSpeedMultipliers ??=
                new Dictionary<string, double>();
            edge.VehicleSpeedMultipliers ??=
                new Dictionary<string, double>();
        }

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

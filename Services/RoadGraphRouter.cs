namespace WardogsNavigator.Services;

/// <summary>
/// Routes strictly on the calibrated/learned road graph.
/// Start/end are projected to the nearest road edge, then graph A* chooses the road sequence.
/// </summary>
public sealed class RoadGraphRouter
{
    public const double DefaultSnapLimitMeters = 260;

    private readonly object _cacheSync = new();
    private readonly Dictionary<string, CachedTopology> _topologyCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GraphPath> _pathCache =
        new(StringComparer.Ordinal);

    public int TopologyCacheHits { get; private set; }
    public int TopologyCacheMisses { get; private set; }
    public int PathCacheHits { get; private set; }
    public int PathCacheMisses { get; private set; }

    public void ResetCacheCounters()
    {
        TopologyCacheHits = 0;
        TopologyCacheMisses = 0;
        PathCacheHits = 0;
        PathCacheMisses = 0;
    }

    public RoadGraphRoute? TryPlan(
        RoadGraph graph,
        MapPoint start,
        MapPoint end,
        RoutePreference preference,
        VehicleRoutingProfile? vehicleProfile = null,
        IReadOnlyList<NavigationHazard>? hazards = null,
        IReadOnlyDictionary<string, double>? visualEdgeRisks = null,
        double baseSpeedKmh = 80,
        double snapLimitMeters = DefaultSnapLimitMeters)
    {
        if (graph.Nodes.Count < 2 || graph.Edges.Count < 1)
            return null;

        vehicleProfile ??= VehicleRoutingProfileService.Generic();
        hazards ??= Array.Empty<NavigationHazard>();
        visualEdgeRisks ??= new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);

        var topology =
            GetTopology(graph);

        var nodes =
            topology.Nodes;

        var usableEdges =
            topology.UsableEdges;

        if (usableEdges.Count == 0)
            return null;

        var adjacency =
            topology.Adjacency;

        var environmentKey =
            BuildEnvironmentKey(
                topology.Key,
                preference,
                vehicleProfile,
                hazards,
                visualEdgeRisks);

        var startSnap = FindNearestEdge(start, usableEdges, nodes);
        var endSnap = FindNearestEdge(end, usableEdges, nodes);

        if (startSnap == null || endSnap == null) return null;
        if (startSnap.DistanceMeters > snapLimitMeters ||
            endSnap.DistanceMeters > snapLimitMeters)
            return null;

        if (startSnap.Edge.Id.Equals(endSnap.Edge.Id, StringComparison.OrdinalIgnoreCase))
        {
            var points = Deduplicate(new[]
            {
                start,
                startSnap.Projected,
                endSnap.Projected,
                end
            });

            return new RoadGraphRoute
            {
                Points = points,
                DistanceKm = PolylineKm(points),
                EstimatedMinutes = EstimateMinutes(
                    points,
                    new[] { startSnap.Edge },
                    vehicleProfile,
                    baseSpeedKmh),
                StartSnapMeters = startSnap.DistanceMeters,
                EndSnapMeters = endSnap.DistanceMeters,
                EdgeCount = 1,
                EdgeIds = new List<string> { startSnap.Edge.Id },
                Confidence = Confidence(
                    startSnap.DistanceMeters,
                    endSnap.DistanceMeters,
                    new[] { startSnap.Edge })
            };
        }

        var starts = new[]
        {
            new EndpointCandidate(startSnap.Edge.A, nodes[startSnap.Edge.A].Position),
            new EndpointCandidate(startSnap.Edge.B, nodes[startSnap.Edge.B].Position)
        };

        var ends = new[]
        {
            new EndpointCandidate(endSnap.Edge.A, nodes[endSnap.Edge.A].Position),
            new EndpointCandidate(endSnap.Edge.B, nodes[endSnap.Edge.B].Position)
        };

        CandidateRoute? best = null;

        foreach (var s in starts)
        {
            foreach (var e in ends)
            {
                var graphPath = FindPath(
                    s.NodeId,
                    e.NodeId,
                    adjacency,
                    nodes,
                    preference,
                    vehicleProfile,
                    hazards,
                    visualEdgeRisks,
                    environmentKey);

                if (graphPath == null) continue;

                var prefix = start.DistanceKm(startSnap.Projected) +
                             startSnap.Projected.DistanceKm(s.Position);
                var suffix = e.Position.DistanceKm(endSnap.Projected) +
                             endSnap.Projected.DistanceKm(end);

                var score = graphPath.Cost + prefix + suffix;
                if (best == null || score < best.Score)
                {
                    best = new CandidateRoute
                    {
                        Score = score,
                        StartEndpoint = s,
                        EndEndpoint = e,
                        Path = graphPath
                    };
                }
            }
        }

        if (best == null) return null;

        var routePoints = new List<MapPoint>
        {
            start,
            startSnap.Projected,
            best.StartEndpoint.Position
        };

        foreach (var nodeId in best.Path.NodeIds)
        {
            if (nodes.TryGetValue(nodeId, out var node))
                routePoints.Add(node.Position);
        }

        routePoints.Add(best.EndEndpoint.Position);
        routePoints.Add(endSnap.Projected);
        routePoints.Add(end);

        routePoints = Deduplicate(routePoints);

        var traversedEdges = new List<RoadEdge> { startSnap.Edge };
        traversedEdges.AddRange(best.Path.Edges);
        traversedEdges.Add(endSnap.Edge);

        return new RoadGraphRoute
        {
            Points = routePoints,
            DistanceKm = PolylineKm(routePoints),
            EstimatedMinutes = EstimateMinutes(
                routePoints,
                traversedEdges,
                vehicleProfile,
                baseSpeedKmh),
            StartSnapMeters = startSnap.DistanceMeters,
            EndSnapMeters = endSnap.DistanceMeters,
            EdgeCount = traversedEdges.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            EdgeIds = traversedEdges.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Confidence = Confidence(
                startSnap.DistanceMeters,
                endSnap.DistanceMeters,
                traversedEdges)
        };
    }

    private GraphPath? FindPath(
        string startId,
        string endId,
        IReadOnlyDictionary<string, List<(string To, RoadEdge Edge)>> adjacency,
        IReadOnlyDictionary<string, RoadNode> nodes,
        RoutePreference preference,
        VehicleRoutingProfile vehicleProfile,
        IReadOnlyList<NavigationHazard> hazards,
        IReadOnlyDictionary<string, double> visualEdgeRisks,
        string environmentKey)
    {
        var cacheKey =
            environmentKey +
            "|" +
            startId +
            ">" +
            endId;

        lock (_cacheSync)
        {
            if (_pathCache.TryGetValue(
                    cacheKey,
                    out var cached))
            {
                PathCacheHits++;
                return ClonePath(cached);
            }

            PathCacheMisses++;
        }

        if (startId.Equals(endId, StringComparison.OrdinalIgnoreCase))
            return new GraphPath
            {
                NodeIds = new List<string> { startId },
                Edges = new List<RoadEdge>(),
                Cost = 0
            };

        var startState =
            new SearchState("", startId);

        var dist =
            new Dictionary<SearchState, double>
            {
                [startState] = 0
            };

        var previous =
            new Dictionary<SearchState, SearchState>();

        var previousEdge =
            new Dictionary<SearchState, RoadEdge>();

        var queue =
            new PriorityQueue<SearchState, double>();

        queue.Enqueue(startState, 0);

        SearchState? goal = null;

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();

            if (!dist.TryGetValue(
                    state,
                    out var currentCost))
                continue;

            if (state.NodeId.Equals(
                    endId,
                    StringComparison.OrdinalIgnoreCase))
            {
                goal = state;
                break;
            }

            if (!adjacency.TryGetValue(
                    state.NodeId,
                    out var neighbours) ||
                !nodes.TryGetValue(
                    state.NodeId,
                    out var fromNode))
                continue;

            foreach (var next in neighbours)
            {
                if (!nodes.TryGetValue(
                        next.To,
                        out var toNode))
                    continue;

                var edgeCost = Cost(
                    next.Edge,
                    fromNode.Position,
                    toNode.Position,
                    preference,
                    vehicleProfile,
                    hazards,
                    visualEdgeRisks);

                var turnCost = TurnPenalty(
                    state.PreviousNodeId,
                    state.NodeId,
                    next.To,
                    nodes,
                    preference);

                var candidate =
                    currentCost +
                    edgeCost +
                    turnCost;

                var nextState =
                    new SearchState(
                        state.NodeId,
                        next.To);

                if (dist.TryGetValue(
                        nextState,
                        out var old) &&
                    candidate >= old)
                    continue;

                dist[nextState] = candidate;
                previous[nextState] = state;
                previousEdge[nextState] =
                    next.Edge;

                var heuristic =
                    toNode.Position.DistanceKm(
                        nodes[endId].Position);

                queue.Enqueue(
                    nextState,
                    candidate +
                    heuristic *
                    HeuristicFactor(preference));
            }
        }

        if (goal is not SearchState goalState ||
            !dist.TryGetValue(
                goalState,
                out var totalCost))
            return null;

        var ids =
            new List<string>
            {
                goalState.NodeId
            };

        var pathEdges =
            new List<RoadEdge>();

        var cursor = goalState;

        while (!cursor.Equals(startState))
        {
            if (!previous.TryGetValue(
                    cursor,
                    out var prev) ||
                !previousEdge.TryGetValue(
                    cursor,
                    out var edge))
                return null;

            pathEdges.Add(edge);
            cursor = prev;
            ids.Add(cursor.NodeId);
        }

        ids.Reverse();
        pathEdges.Reverse();

        var result =
            new GraphPath
            {
                NodeIds = ids,
                Edges = pathEdges,
                Cost = totalCost
            };

        lock (_cacheSync)
        {
            if (_pathCache.Count > 768)
                _pathCache.Clear();

            _pathCache[cacheKey] =
                ClonePath(result);
        }

        return result;
    }

    private CachedTopology GetTopology(
        RoadGraph graph)
    {
        var key =
            graph.MapId +
            "|" +
            graph.UpdatedUtc.Ticks +
            "|" +
            graph.Nodes.Count +
            "|" +
            graph.Edges.Count;

        lock (_cacheSync)
        {
            if (_topologyCache.TryGetValue(
                    key,
                    out var cached))
            {
                TopologyCacheHits++;
                return cached;
            }

            TopologyCacheMisses++;
        }

        var nodes =
            graph.Nodes.ToDictionary(
                n => n.Id,
                StringComparer.OrdinalIgnoreCase);

        var usableEdges =
            graph.Edges
                .Where(e =>
                    !e.Blocked &&
                    nodes.ContainsKey(e.A) &&
                    nodes.ContainsKey(e.B))
                .ToList();

        var topology =
            new CachedTopology
            {
                Key = key,
                Nodes = nodes,
                UsableEdges = usableEdges,
                Adjacency =
                    BuildAdjacency(
                        usableEdges)
            };

        lock (_cacheSync)
        {
            if (_topologyCache.Count > 12)
            {
                _topologyCache.Clear();
                _pathCache.Clear();
            }

            _topologyCache[key] =
                topology;
        }

        return topology;
    }

    private static string BuildEnvironmentKey(
        string topologyKey,
        RoutePreference preference,
        VehicleRoutingProfile vehicleProfile,
        IReadOnlyList<NavigationHazard> hazards,
        IReadOnlyDictionary<string, double> visualEdgeRisks)
    {
        var hazardSignature =
            string.Join(
                ";",
                hazards
                    .Where(h =>
                        h.ExpiresUtc >
                        DateTime.UtcNow)
                    .OrderBy(h =>
                        h.Center.X)
                    .ThenBy(h =>
                        h.Center.Y)
                    .Select(h =>
                        Math.Round(
                            h.Center.X,
                            2) +
                        "," +
                        Math.Round(
                            h.Center.Y,
                            2) +
                        "," +
                        Math.Round(
                            h.RadiusMeters,
                            0) +
                        "," +
                        Math.Round(
                            h.Severity,
                            2)));

        var visionSignature =
            string.Join(
                ";",
                visualEdgeRisks
                    .OrderBy(x => x.Key)
                    .Select(x =>
                        x.Key +
                        ":" +
                        Math.Round(
                            x.Value,
                            2)));

        return
            topologyKey +
            "|" +
            preference +
            "|" +
            vehicleProfile.VehicleId +
            "|H:" +
            hazardSignature +
            "|V:" +
            visionSignature;
    }

    private static GraphPath ClonePath(
        GraphPath source) =>
        new()
        {
            NodeIds =
                source.NodeIds.ToList(),
            Edges =
                source.Edges.ToList(),
            Cost = source.Cost
        };

    private static Dictionary<string, List<(string To, RoadEdge Edge)>> BuildAdjacency(
        IEnumerable<RoadEdge> edges)
    {
        var adjacency =
            new Dictionary<string, List<(string To, RoadEdge Edge)>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(
                    edge.A,
                    out var aList))
                adjacency[edge.A] =
                    aList = new();

            if (!adjacency.TryGetValue(
                    edge.B,
                    out var bList))
                adjacency[edge.B] =
                    bList = new();

            aList.Add((edge.B, edge));
            bList.Add((edge.A, edge));
        }

        return adjacency;
    }

    private static double TurnPenalty(
        string previousId,
        string currentId,
        string nextId,
        IReadOnlyDictionary<string, RoadNode> nodes,
        RoutePreference preference)
    {
        if (preference == RoutePreference.Shortest ||
            string.IsNullOrWhiteSpace(previousId) ||
            !nodes.TryGetValue(previousId, out var previous) ||
            !nodes.TryGetValue(currentId, out var current) ||
            !nodes.TryGetValue(nextId, out var next))
            return 0;

        var incoming =
            previous.Position.BearingDegTo(
                current.Position);

        var outgoing =
            current.Position.BearingDegTo(
                next.Position);

        var delta = Math.Abs(
            NormalizeSigned(
                outgoing - incoming));

        var basePenalty = delta switch
        {
            < 25 => 0.0,
            < 55 => 0.008,
            < 105 => 0.024,
            < 150 => 0.060,
            _ => 0.180
        };

        return preference == RoutePreference.Safe
            ? basePenalty * 0.75
            : basePenalty;
    }

    private static double Cost(
        RoadEdge edge,
        MapPoint a,
        MapPoint b,
        RoutePreference preference,
        VehicleRoutingProfile vehicleProfile,
        IReadOnlyList<NavigationHazard> hazards,
        IReadOnlyDictionary<string, double> visualEdgeRisks)
    {
        var km = a.DistanceKm(b);

        var speedFactor = LearnedSpeedFactor(
            edge,
            vehicleProfile);

        var hazardPenalty = hazards
            .Where(h => h.ExpiresUtc > DateTime.UtcNow)
            .Where(h =>
                h.Center.DistanceMeters(Project(h.Center, a, b)) <=
                h.RadiusMeters)
            .Select(h => Math.Clamp(h.Severity, 0, 1))
            .DefaultIfEmpty(0)
            .Max();

        var autoPenalty = edge.Source.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? (1.0 - Math.Clamp(edge.AutoScore, 0, 1)) * 0.85 + 0.18
            : 0.0;

        var confidencePenalty =
            (edge.Verified ? 0.0 : 0.45) +
            (edge.Traversals > 0 ? 0.0 : 0.18) +
            autoPenalty;

        var staticRisk = Math.Clamp(
            edge.Risk + edge.AiRiskAdjustment,
            0,
            1);

        var visualRisk = visualEdgeRisks.TryGetValue(
            edge.Id,
            out var visual)
            ? Math.Clamp(visual, 0, 1)
            : 0;

        var effectiveRisk = Math.Clamp(
            staticRisk +
            visualRisk * 1.25 +
            hazardPenalty *
            (1.15 - vehicleProfile.RiskTolerance * 0.55),
            0,
            2.2);

        return preference switch
        {
            RoutePreference.Shortest =>
                km * (1.0 + hazardPenalty * 0.35),

            RoutePreference.Safe =>
                km / Math.Max(0.25, speedFactor) *
                (
                    1.0 +
                    effectiveRisk * 4.2 +
                    confidencePenalty +
                    (edge.Class == RoadClass.Track
                        ? Math.Max(0.0, 0.50 - vehicleProfile.TrackFactor * 0.25)
                        : 0)
                ),

            _ =>
                km / Math.Max(0.25, speedFactor) *
                (
                    1.0 +
                    confidencePenalty * 0.25 +
                    effectiveRisk * 0.70
                )
        };
    }


    private static double LearnedSpeedFactor(
        RoadEdge edge,
        VehicleRoutingProfile profile)
    {
        var baseFactor =
            profile.FactorFor(edge.Class);

        var localMultiplier = 1.0;
        var localConfidence = 0.0;

        if (edge.LocalVehicleSpeedLearning.TryGetValue(
                profile.VehicleId,
                out var state))
        {
            localMultiplier =
                Math.Clamp(
                    state.MeanMultiplier,
                    0.55,
                    1.25);

            localConfidence =
                Math.Clamp(
                    state.Confidence,
                    0,
                    1);
        }
        else if (edge.LocalVehicleSpeedMultipliers.TryGetValue(
                     profile.VehicleId,
                     out var legacyLocal))
        {
            localMultiplier =
                Math.Clamp(
                    legacyLocal,
                    0.55,
                    1.25);

            // Old data has useful evidence, but no explicit confidence.
            localConfidence = 0.42;
        }

        var aiMultiplier = 1.0;
        var aiConfidence = 0.0;

        if (edge.VehicleSpeedMultipliers.TryGetValue(
                profile.VehicleId,
                out var ai))
        {
            aiMultiplier =
                Math.Clamp(
                    ai,
                    0.55,
                    1.25);

            aiConfidence =
                edge.VehicleAiConfidences.TryGetValue(
                    profile.VehicleId,
                    out var vehicleConfidence)
                    ? Math.Clamp(
                        vehicleConfidence,
                        0,
                        1)
                    : Math.Clamp(
                        edge.AiConfidence,
                        0,
                        1);
        }

        // The baseline always keeps some weight. Real driving can earn much
        // more influence than AI, while low-confidence observations remain
        // close to the original vehicle/road model.
        var localWeight =
            localConfidence * 0.75;

        var aiWeight =
            aiConfidence * 0.30;

        var baseWeight =
            Math.Max(
                0.15,
                1.0 -
                localWeight -
                aiWeight);

        var totalWeight =
            baseWeight +
            localWeight +
            aiWeight;

        var learned =
            (
                baseWeight +
                localMultiplier * localWeight +
                aiMultiplier * aiWeight
            ) /
            totalWeight;

        return baseFactor *
               Math.Clamp(
                   learned,
                   0.55,
                   1.25);
    }

    private static double HeuristicFactor(RoutePreference preference) =>
        preference == RoutePreference.Safe ? 0.35 : 0.75;

    private static EdgeSnap? FindNearestEdge(
        MapPoint point,
        IReadOnlyList<RoadEdge> edges,
        IReadOnlyDictionary<string, RoadNode> nodes)
    {
        EdgeSnap? best = null;

        foreach (var edge in edges)
        {
            if (!nodes.TryGetValue(edge.A, out var a) ||
                !nodes.TryGetValue(edge.B, out var b))
                continue;

            var projected = Project(point, a.Position, b.Position);
            var distance = point.DistanceMeters(projected);

            if (best == null || distance < best.DistanceMeters)
            {
                best = new EdgeSnap
                {
                    Edge = edge,
                    Projected = projected,
                    DistanceMeters = distance
                };
            }
        }

        return best;
    }

    private static MapPoint Project(MapPoint p, MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var denom = dx * dx + dy * dy;

        if (denom < 1e-12) return a;

        var t = Math.Clamp(
            ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / denom,
            0,
            1);

        return new MapPoint(
            a.X + t * dx,
            a.Y + t * dy);
    }

    private static double Confidence(
        double startSnapMeters,
        double endSnapMeters,
        IEnumerable<RoadEdge> edges)
    {
        var list = edges
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (list.Count == 0) return 0;

        var verified = list.Count(e => e.Verified) / (double)list.Count;
        var learned = list.Count(e => e.Traversals > 0) / (double)list.Count;
        var auto = list
            .Where(e => e.Source.Equals("auto", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var autoQuality = auto.Count == 0
            ? 0.0
            : auto.Average(e => Math.Clamp(e.AutoScore, 0, 1));

        var snap = 1.0 - Math.Clamp(
            (startSnapMeters + endSnapMeters) / (DefaultSnapLimitMeters * 2.0),
            0,
            1);

        return Math.Clamp(
            verified * 0.43 +
            learned * 0.22 +
            snap * 0.23 +
            autoQuality * 0.12,
            0,
            1);
    }


    private static double EstimateMinutes(
        IReadOnlyList<MapPoint> routePoints,
        IEnumerable<RoadEdge> edges,
        VehicleRoutingProfile profile,
        double baseSpeedKmh)
    {
        var km = PolylineKm(routePoints);
        if (km <= 0 || baseSpeedKmh <= 1) return 0;

        var distinct = edges
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (distinct.Count == 0)
            return km / baseSpeedKmh * 60.0;

        var factors = distinct.Select(edge =>
        {
            return Math.Clamp(
                LearnedSpeedFactor(
                    edge,
                    profile),
                0.25,
                1.35);
        });

        var averageFactor = Math.Max(0.25, factors.Average());
        return km / (baseSpeedKmh * averageFactor) * 60.0;
    }

    private static List<MapPoint> Deduplicate(IEnumerable<MapPoint> points)
    {
        var result = new List<MapPoint>();

        foreach (var p in points)
        {
            if (result.Count == 0 || result[^1].DistanceMeters(p) > 2.5)
                result.Add(p);
        }

        return result;
    }

    private static double PolylineKm(IReadOnlyList<MapPoint> points)
    {
        double km = 0;
        for (var i = 0; i + 1 < points.Count; i++)
            km += points[i].DistanceKm(points[i + 1]);
        return km;
    }

    private static double NormalizeSigned(double degrees)
    {
        degrees %= 360;
        if (degrees > 180) degrees -= 360;
        if (degrees < -180) degrees += 360;
        return degrees;
    }

    private readonly record struct SearchState(
        string PreviousNodeId,
        string NodeId);

    private sealed class CachedTopology
    {
        public string Key { get; init; } = "";
        public IReadOnlyDictionary<string, RoadNode> Nodes { get; init; } =
            new Dictionary<string, RoadNode>();
        public IReadOnlyList<RoadEdge> UsableEdges { get; init; } =
            Array.Empty<RoadEdge>();
        public IReadOnlyDictionary<string, List<(string To, RoadEdge Edge)>> Adjacency { get; init; } =
            new Dictionary<string, List<(string To, RoadEdge Edge)>>();
    }

    private sealed class EdgeSnap
    {
        public RoadEdge Edge { get; set; } = new();
        public MapPoint Projected { get; set; }
        public double DistanceMeters { get; set; }
    }

    private sealed class EndpointCandidate
    {
        public string NodeId { get; }
        public MapPoint Position { get; }

        public EndpointCandidate(string nodeId, MapPoint position)
        {
            NodeId = nodeId;
            Position = position;
        }
    }

    private sealed class CandidateRoute
    {
        public double Score { get; set; }
        public EndpointCandidate StartEndpoint { get; set; } = null!;
        public EndpointCandidate EndEndpoint { get; set; } = null!;
        public GraphPath Path { get; set; } = null!;
    }

    private sealed class GraphPath
    {
        public List<string> NodeIds { get; set; } = new();
        public List<RoadEdge> Edges { get; set; } = new();
        public double Cost { get; set; }
    }
}

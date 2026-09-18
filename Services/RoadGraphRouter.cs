namespace WardogsNavigator.Services;

/// <summary>
/// Routes strictly on the calibrated/learned road graph.
/// Start/end are projected to the nearest road edge, then graph A* chooses the road sequence.
/// </summary>
public sealed class RoadGraphRouter
{
    public const double DefaultSnapLimitMeters = 260;

    public RoadGraphRoute? TryPlan(
        RoadGraph graph,
        MapPoint start,
        MapPoint end,
        RoutePreference preference,
        double snapLimitMeters = DefaultSnapLimitMeters)
    {
        if (graph.Nodes.Count < 2 || graph.Edges.Count < 1)
            return null;

        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var usableEdges = graph.Edges
            .Where(e =>
                !e.Blocked &&
                nodes.ContainsKey(e.A) &&
                nodes.ContainsKey(e.B))
            .ToList();

        if (usableEdges.Count == 0) return null;

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
                StartSnapMeters = startSnap.DistanceMeters,
                EndSnapMeters = endSnap.DistanceMeters,
                EdgeCount = 1,
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
                    usableEdges,
                    nodes,
                    preference);

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
            StartSnapMeters = startSnap.DistanceMeters,
            EndSnapMeters = endSnap.DistanceMeters,
            EdgeCount = traversedEdges.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Confidence = Confidence(
                startSnap.DistanceMeters,
                endSnap.DistanceMeters,
                traversedEdges)
        };
    }

    private static GraphPath? FindPath(
        string startId,
        string endId,
        IReadOnlyList<RoadEdge> edges,
        IReadOnlyDictionary<string, RoadNode> nodes,
        RoutePreference preference)
    {
        if (startId.Equals(endId, StringComparison.OrdinalIgnoreCase))
            return new GraphPath
            {
                NodeIds = new List<string> { startId },
                Edges = new List<RoadEdge>(),
                Cost = 0
            };

        var adjacency = new Dictionary<string, List<(string To, RoadEdge Edge)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.A, out var aList))
                adjacency[edge.A] = aList = new();
            if (!adjacency.TryGetValue(edge.B, out var bList))
                adjacency[edge.B] = bList = new();

            aList.Add((edge.B, edge));
            bList.Add((edge.A, edge));
        }

        var dist = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [startId] = 0
        };
        var prevNode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var prevEdge = new Dictionary<string, RoadEdge>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(startId, 0);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Equals(endId, StringComparison.OrdinalIgnoreCase))
                break;

            if (!dist.TryGetValue(current, out var currentCost))
                continue;
            if (!adjacency.TryGetValue(current, out var neighbours))
                continue;

            foreach (var next in neighbours)
            {
                if (!nodes.TryGetValue(current, out var fromNode) ||
                    !nodes.TryGetValue(next.To, out var toNode))
                    continue;

                var edgeCost = Cost(next.Edge, fromNode.Position, toNode.Position, preference);
                var candidate = currentCost + edgeCost;

                if (dist.TryGetValue(next.To, out var old) && candidate >= old)
                    continue;

                dist[next.To] = candidate;
                prevNode[next.To] = current;
                prevEdge[next.To] = next.Edge;

                var heuristic = toNode.Position.DistanceKm(nodes[endId].Position);
                queue.Enqueue(next.To, candidate + heuristic * HeuristicFactor(preference));
            }
        }

        if (!dist.TryGetValue(endId, out var totalCost))
            return null;

        var ids = new List<string> { endId };
        var pathEdges = new List<RoadEdge>();
        var cursor = endId;

        while (!cursor.Equals(startId, StringComparison.OrdinalIgnoreCase))
        {
            if (!prevNode.TryGetValue(cursor, out var previous) ||
                !prevEdge.TryGetValue(cursor, out var edge))
                return null;

            pathEdges.Add(edge);
            cursor = previous;
            ids.Add(cursor);
        }

        ids.Reverse();
        pathEdges.Reverse();

        return new GraphPath
        {
            NodeIds = ids,
            Edges = pathEdges,
            Cost = totalCost
        };
    }

    private static double Cost(
        RoadEdge edge,
        MapPoint a,
        MapPoint b,
        RoutePreference preference)
    {
        var km = a.DistanceKm(b);
        var speedFactor = edge.Class switch
        {
            RoadClass.Primary => 1.00,
            RoadClass.Bridge => 0.90,
            RoadClass.Secondary => 0.82,
            RoadClass.Track => 0.58,
            _ => 0.80
        };

        var autoPenalty = edge.Source.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? (1.0 - Math.Clamp(edge.AutoScore, 0, 1)) * 0.85 + 0.18
            : 0.0;

        var confidencePenalty =
            (edge.Verified ? 0.0 : 0.45) +
            (edge.Traversals > 0 ? 0.0 : 0.18) +
            autoPenalty;

        return preference switch
        {
            RoutePreference.Shortest => km,
            RoutePreference.Safe =>
                km * (
                    1.0 +
                    Math.Clamp(edge.Risk, 0, 1) * 3.5 +
                    confidencePenalty +
                    (edge.Class == RoadClass.Track ? 0.45 : 0)),
            _ =>
                km / Math.Max(0.25, speedFactor) *
                (1.0 + confidencePenalty * 0.25)
        };
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

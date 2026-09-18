using SixLabors.ImageSharp.PixelFormats;

namespace WardogsNavigator.Services;

public sealed class AutoRoadExtractionResult
{
    public RoadGraph Graph { get; set; } = new();
    public int CandidateBlocks { get; set; }
    public int AcceptedNodes { get; set; }
    public int AcceptedEdges { get; set; }
    public double AverageScore { get; set; }
}

/// <summary>
/// Builds an initial road graph from the tactical map image.
/// The result is heuristic and intentionally marked Source=auto, Verified=false.
/// </summary>
public sealed class AutoRoadExtractor
{
    private readonly MapAssetService _assets;

    private const int Grid = 384;
    private const int Block = 12;
    private const float NodeThreshold = 0.64f;
    private const float EdgeThreshold = 0.56f;

    public AutoRoadExtractor(MapAssetService assets)
    {
        _assets = assets;
    }

    public async Task<AutoRoadExtractionResult> ExtractAsync(
        string mapId,
        CancellationToken cancellationToken = default)
    {
        var path = await _assets.GetMapWebpPathAsync(mapId, cancellationToken);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
            var score = BuildScoreGrid(image, cancellationToken);
            var smoothed = Smooth(score);

            var candidates = ExtractCandidates(smoothed);
            var graph = new RoadGraph
            {
                MapId = mapId,
                Version = 3
            };

            var nodeByCandidate = new Dictionary<int, RoadNode>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var node = new RoadNode
                {
                    Id = "auto_n_" + i.ToString("D4"),
                    Position = CellToMap(c.X, c.Y)
                };
                graph.Nodes.Add(node);
                nodeByCandidate[i] = node;
            }

            var edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            double scoreSum = 0;
            var acceptedEdges = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var a = candidates[i];

                var neighbours = candidates
                    .Select((c, idx) => new
                    {
                        Candidate = c,
                        Index = idx,
                        Dist = Math.Sqrt(
                            Math.Pow(c.X - a.X, 2) +
                            Math.Pow(c.Y - a.Y, 2))
                    })
                    .Where(x => x.Index != i && x.Dist <= Block * 2.4)
                    .OrderBy(x => x.Dist)
                    .Take(7)
                    .ToList();

                foreach (var n in neighbours)
                {
                    var min = Math.Min(i, n.Index);
                    var max = Math.Max(i, n.Index);
                    var key = min + ":" + max;
                    if (!edgeKeys.Add(key)) continue;

                    var lineScore = SampleLineScore(
                        smoothed,
                        a.X,
                        a.Y,
                        n.Candidate.X,
                        n.Candidate.Y);

                    if (lineScore < EdgeThreshold) continue;

                    var edge = new RoadEdge
                    {
                        Id = "auto_e_" + min.ToString("D4") + "_" + max.ToString("D4"),
                        A = nodeByCandidate[i].Id,
                        B = nodeByCandidate[n.Index].Id,
                        Class = ClassForScore((a.Score + n.Candidate.Score + lineScore) / 3f),
                        Verified = false,
                        Blocked = false,
                        Risk = 0,
                        Traversals = 0,
                        AutoScore = lineScore,
                        Source = "auto"
                    };

                    graph.Edges.Add(edge);
                    scoreSum += lineScore;
                    acceptedEdges++;
                }
            }

            RemoveIsolatedNodes(graph);
            PruneTinyComponents(graph, minimumNodes: 3);

            return new AutoRoadExtractionResult
            {
                Graph = graph,
                CandidateBlocks = (Grid / Block) * (Grid / Block),
                AcceptedNodes = graph.Nodes.Count,
                AcceptedEdges = graph.Edges.Count,
                AverageScore = acceptedEdges > 0 ? scoreSum / acceptedEdges : 0
            };
        }, cancellationToken);
    }

    private static float[] BuildScoreGrid(
        SixLabors.ImageSharp.Image<Rgba32> image,
        CancellationToken ct)
    {
        var result = new float[Grid * Grid];

        for (var gy = 0; gy < Grid; gy++)
        {
            if ((gy & 15) == 0) ct.ThrowIfCancellationRequested();

            var py = Math.Clamp(
                (int)((gy + 0.5) / Grid * image.Height),
                0,
                image.Height - 1);

            for (var gx = 0; gx < Grid; gx++)
            {
                var px = Math.Clamp(
                    (int)((gx + 0.5) / Grid * image.Width),
                    0,
                    image.Width - 1);

                result[gy * Grid + gx] = RoadLikelihood(image[px, py]);
            }
        }

        return result;
    }

    private static float RoadLikelihood(Rgba32 p)
    {
        var r = p.R / 255f;
        var g = p.G / 255f;
        var b = p.B / 255f;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var saturation = max - min;
        var luminance = r * 0.299f + g * 0.587f + b * 0.114f;

        var neutral = 1f - Math.Clamp(saturation / 0.32f, 0f, 1f);
        var medium = 1f - Math.Clamp(Math.Abs(luminance - 0.56f) / 0.50f, 0f, 1f);

        var road = neutral * 0.68f + medium * 0.32f;

        // Penalize pixels likely to be water/deep shadow.
        if (b > r * 1.13f && b > g * 1.08f)
            road *= 0.15f;
        if (luminance < 0.13f)
            road *= 0.30f;

        return Math.Clamp(road, 0f, 1f);
    }

    private static float[] Smooth(float[] input)
    {
        var output = new float[input.Length];

        for (var y = 0; y < Grid; y++)
        {
            for (var x = 0; x < Grid; x++)
            {
                double sum = 0;
                var count = 0;

                for (var dy = -1; dy <= 1; dy++)
                {
                    var yy = y + dy;
                    if ((uint)yy >= Grid) continue;

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var xx = x + dx;
                        if ((uint)xx >= Grid) continue;
                        sum += input[yy * Grid + xx];
                        count++;
                    }
                }

                output[y * Grid + x] = (float)(sum / Math.Max(1, count));
            }
        }

        return output;
    }

    private static List<Candidate> ExtractCandidates(float[] score)
    {
        var result = new List<Candidate>();

        for (var by = 0; by < Grid; by += Block)
        {
            for (var bx = 0; bx < Grid; bx += Block)
            {
                var bestScore = 0f;
                var bestX = -1;
                var bestY = -1;

                for (var y = by; y < Math.Min(Grid, by + Block); y++)
                {
                    for (var x = bx; x < Math.Min(Grid, bx + Block); x++)
                    {
                        var s = score[y * Grid + x];
                        if (s <= bestScore) continue;

                        bestScore = s;
                        bestX = x;
                        bestY = y;
                    }
                }

                if (bestX >= 0 && bestScore >= NodeThreshold)
                {
                    result.Add(new Candidate
                    {
                        X = bestX,
                        Y = bestY,
                        Score = bestScore
                    });
                }
            }
        }

        return result;
    }

    private static float SampleLineScore(
        float[] score,
        int x0,
        int y0,
        int x1,
        int y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps <= 0) return score[y0 * Grid + x0];

        double sum = 0;
        var lowCount = 0;

        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var x = Math.Clamp((int)Math.Round(x0 + dx * t), 0, Grid - 1);
            var y = Math.Clamp((int)Math.Round(y0 + dy * t), 0, Grid - 1);
            var s = score[y * Grid + x];

            sum += s;
            if (s < 0.42f) lowCount++;
        }

        var average = (float)(sum / (steps + 1));
        var lowFraction = lowCount / (float)(steps + 1);

        return Math.Clamp(
            average - lowFraction * 0.35f,
            0f,
            1f);
    }

    private static RoadClass ClassForScore(float score)
    {
        if (score >= 0.82f) return RoadClass.Primary;
        if (score >= 0.70f) return RoadClass.Secondary;
        return RoadClass.Track;
    }

    private static MapPoint CellToMap(int x, int y)
    {
        return new MapPoint(
            x / (double)(Grid - 1) * MapPoint.MapSize,
            MapPoint.MapSize -
            y / (double)(Grid - 1) * MapPoint.MapSize);
    }

    private static void RemoveIsolatedNodes(RoadGraph graph)
    {
        var used = graph.Edges
            .SelectMany(e => new[] { e.A, e.B })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        graph.Nodes.RemoveAll(n => !used.Contains(n.Id));
    }

    private static void PruneTinyComponents(RoadGraph graph, int minimumNodes)
    {
        var adjacency = graph.Nodes.ToDictionary(
            n => n.Id,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges)
        {
            if (!adjacency.ContainsKey(edge.A) ||
                !adjacency.ContainsKey(edge.B))
                continue;

            adjacency[edge.A].Add(edge.B);
            adjacency[edge.B].Add(edge.A);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            if (!visited.Add(node.Id)) continue;

            var component = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(node.Id);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                component.Add(id);

                foreach (var next in adjacency[id])
                {
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            if (component.Count >= minimumNodes)
            {
                foreach (var id in component)
                    keep.Add(id);
            }
        }

        graph.Edges.RemoveAll(e => !keep.Contains(e.A) || !keep.Contains(e.B));
        graph.Nodes.RemoveAll(n => !keep.Contains(n.Id));
    }

    private sealed class Candidate
    {
        public int X { get; set; }
        public int Y { get; set; }
        public float Score { get; set; }
    }
}

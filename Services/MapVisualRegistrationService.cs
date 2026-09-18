using System.Drawing;
using System.Drawing.Drawing2D;

namespace WardogsNavigator.Services;

/// <summary>
/// Local visual registration between the in-game map viewport and the cached
/// tactical map. The transform supports translation, scale and arbitrary
/// rotation. Global acquisition keeps several coarse hypotheses and refines
/// them independently so repetitive road patterns do not trap the search in
/// the first local maximum.
/// </summary>
public sealed class MapVisualRegistrationService
{
    private readonly MapAssetService _maps;
    private readonly Dictionary<string, FeatureImage> _baseFeatures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MapVisualRegistrationService(MapAssetService maps)
    {
        _maps = maps;
    }

    public async Task<MapViewportRegistration?> RegisterAsync(
        string mapId,
        Bitmap screenshot,
        MapViewportRegistration? hint = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var baseFeature = await GetBaseFeatureAsync(
                mapId,
                cancellationToken);

            if (baseFeature == null)
                return null;

            return await Task.Run(
                () => RegisterFeatures(
                    mapId,
                    baseFeature,
                    screenshot,
                    hint,
                    cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public MapViewportRegistration? RegisterLocal(
        string mapId,
        Bitmap baseMap,
        Bitmap screenshot,
        MapViewportRegistration? hint = null,
        CancellationToken cancellationToken = default)
    {
        var baseFeature = FeatureImage.FromBitmap(baseMap, 220);
        return RegisterFeatures(
            mapId,
            baseFeature,
            screenshot,
            hint,
            cancellationToken);
    }

    private static MapViewportRegistration? RegisterFeatures(
        string mapId,
        FeatureImage baseFeature,
        Bitmap screenshot,
        MapViewportRegistration? hint,
        CancellationToken cancellationToken)
    {
        var screenFeature = FeatureImage.FromBitmap(screenshot, 160);

        if (hint?.IsValid == true)
        {
            var local = SearchNearHint(
                screenFeature,
                baseFeature,
                hint,
                cancellationToken);

            if (local.Score >= 0.38)
                return ToRegistration(mapId, local);
        }

        var seeds = SearchGlobalCandidates(
            screenFeature,
            baseFeature,
            cancellationToken);

        if (seeds.Count == 0)
            return null;

        Candidate best = default;
        best = best with { Score = -1 };

        foreach (var seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var refined = Refine(
                screenFeature,
                baseFeature,
                seed,
                cancellationToken);

            // Final comparison uses a denser sampling grid than the coarse
            // acquisition. This is important on maps with repeated road
            // motifs where several coarse candidates can look similar.
            var finalScore = Score(
                screenFeature,
                baseFeature,
                refined.Left,
                refined.Top,
                refined.Width,
                refined.Height,
                refined.RotationDeg,
                30);

            refined = refined with { Score = finalScore };

            if (refined.Score > best.Score)
                best = refined;
        }

        return best.Score <= -0.90
            ? null
            : ToRegistration(mapId, best);
    }

    private async Task<FeatureImage?> GetBaseFeatureAsync(
        string mapId,
        CancellationToken cancellationToken)
    {
        if (_baseFeatures.TryGetValue(mapId, out var cached))
            return cached;

        var bitmap = await _maps.GetBitmapAsync(
            mapId,
            cancellationToken);

        if (bitmap == null)
            return null;

        var feature = FeatureImage.FromBitmap(bitmap, 220);
        _baseFeatures[mapId] = feature;
        return feature;
    }

    private static Candidate SearchNearHint(
        FeatureImage screen,
        FeatureImage map,
        MapViewportRegistration hint,
        CancellationToken ct)
    {
        var candidates = new List<Candidate>();

        var widthFactors = new[]
        {
            0.90, 0.95, 1.00, 1.05, 1.10
        };

        var rotationOffsets = new[]
        {
            -18.0, -12.0, -6.0, 0.0, 6.0, 12.0, 18.0
        };

        foreach (var widthFactor in widthFactors)
        {
            ct.ThrowIfCancellationRequested();

            var width = Math.Clamp(
                hint.Width01 * widthFactor,
                0.15,
                1.0);

            var height = HeightFor(width, screen, map);
            if (height > 1)
                continue;

            var stepX = Math.Max(0.005, width * 0.050);
            var stepY = Math.Max(0.005, height * 0.050);

            foreach (var offset in rotationOffsets)
            {
                var rotation = NormalizeRotation(
                    hint.RotationDeg + offset);

                for (var oy = -2; oy <= 2; oy++)
                {
                    for (var ox = -2; ox <= 2; ox++)
                    {
                        var left = Math.Clamp(
                            hint.Left01 + ox * stepX,
                            0,
                            Math.Max(0, 1 - width));

                        var top = Math.Clamp(
                            hint.Top01 + oy * stepY,
                            0,
                            Math.Max(0, 1 - height));

                        if (!CandidateInsideMap(
                                left,
                                top,
                                width,
                                height,
                                rotation))
                            continue;

                        var score = Score(
                            screen,
                            map,
                            left,
                            top,
                            width,
                            height,
                            rotation,
                            22);

                        AddCandidate(
                            candidates,
                            new Candidate(
                                left,
                                top,
                                width,
                                height,
                                rotation,
                                score),
                            5);
                    }
                }
            }
        }

        if (candidates.Count == 0)
            return new Candidate(0, 0, 1, 1, 0, -1);

        Candidate best = new(0, 0, 1, 1, 0, -1);

        foreach (var seed in candidates)
        {
            var refined = Refine(screen, map, seed, ct);
            if (refined.Score > best.Score)
                best = refined;
        }

        return best;
    }

    private static List<Candidate> SearchGlobalCandidates(
        FeatureImage screen,
        FeatureImage map,
        CancellationToken ct)
    {
        const int keep = 10;
        var best = new List<Candidate>(keep);

        var widths = new[]
        {
            1.00, 0.90, 0.80, 0.70, 0.61, 0.54, 0.48,
            0.43, 0.38, 0.34, 0.30, 0.27, 0.24
        };

        // 30-degree global seeds guarantee that a 31-degree view starts near
        // the correct basin. The expensive fine search only runs for Top-N
        // candidates and remembered views use SearchNearHint instead.
        var rotations = new[]
        {
            0.0,
            30.0,
            60.0,
            90.0,
            120.0,
            150.0,
            180.0,
            -150.0,
            -120.0,
            -90.0,
            -60.0,
            -30.0
        };

        foreach (var width in widths)
        {
            ct.ThrowIfCancellationRequested();

            var height = HeightFor(width, screen, map);
            if (height > 1.001)
                continue;

            // Denser spatial seeds than v0.9.0's original 15% step. Refine()
            // can then converge without needing to jump between distant local
            // maxima.
            var stepX = Math.Max(0.012, width * 0.115);
            var stepY = Math.Max(0.012, height * 0.115);

            var xs = Positions(Math.Max(0, 1 - width), stepX);
            var ys = Positions(Math.Max(0, 1 - height), stepY);

            foreach (var rotation in rotations)
            {
                foreach (var top in ys)
                {
                    foreach (var left in xs)
                    {
                        if (!CandidateInsideMap(
                                left,
                                top,
                                width,
                                height,
                                rotation))
                            continue;

                        var score = Score(
                            screen,
                            map,
                            left,
                            top,
                            width,
                            height,
                            rotation,
                            20);

                        AddCandidate(
                            best,
                            new Candidate(
                                left,
                                top,
                                width,
                                height,
                                rotation,
                                score),
                            keep);
                    }
                }
            }
        }

        return best
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static void AddCandidate(
        List<Candidate> candidates,
        Candidate candidate,
        int keep)
    {
        if (!double.IsFinite(candidate.Score) || candidate.Score <= -0.95)
            return;

        // Avoid spending refinement work on several virtually identical
        // hypotheses from neighbouring coarse grid cells.
        var duplicate = candidates.Any(x =>
            Math.Abs(x.Left - candidate.Left) < 0.018 &&
            Math.Abs(x.Top - candidate.Top) < 0.018 &&
            Math.Abs(x.Width - candidate.Width) < 0.025 &&
            RotationDistance(x.RotationDeg, candidate.RotationDeg) < 12);

        if (duplicate)
        {
            var index = candidates.FindIndex(x =>
                Math.Abs(x.Left - candidate.Left) < 0.018 &&
                Math.Abs(x.Top - candidate.Top) < 0.018 &&
                Math.Abs(x.Width - candidate.Width) < 0.025 &&
                RotationDistance(x.RotationDeg, candidate.RotationDeg) < 12);

            if (index >= 0 && candidate.Score > candidates[index].Score)
                candidates[index] = candidate;
        }
        else
        {
            candidates.Add(candidate);
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (candidates.Count > keep)
            candidates.RemoveRange(keep, candidates.Count - keep);
    }

    private static Candidate Refine(
        FeatureImage screen,
        FeatureImage map,
        Candidate seed,
        CancellationToken ct)
    {
        var best = seed;

        for (var pass = 0; pass < 4; pass++)
        {
            ct.ThrowIfCancellationRequested();

            var scaleStep = pass switch
            {
                0 => 0.055,
                1 => 0.026,
                2 => 0.012,
                _ => 0.005
            };

            var rotationStep = pass switch
            {
                0 => 10.0,
                1 => 4.0,
                2 => 1.5,
                _ => 0.6
            };

            var moveX = Math.Max(0.0015, best.Width * scaleStep);
            var moveY = Math.Max(0.0015, best.Height * scaleStep);
            var current = best;

            for (var sw = -1; sw <= 1; sw++)
            {
                var width = Math.Clamp(
                    current.Width * (1 + sw * scaleStep),
                    0.14,
                    1.0);

                var height = HeightFor(width, screen, map);
                if (height > 1)
                    continue;

                for (var sr = -2; sr <= 2; sr++)
                {
                    var rotation = NormalizeRotation(
                        current.RotationDeg + sr * rotationStep);

                    for (var oy = -2; oy <= 2; oy++)
                    {
                        for (var ox = -2; ox <= 2; ox++)
                        {
                            var left = Math.Clamp(
                                current.Left + ox * moveX,
                                0,
                                Math.Max(0, 1 - width));

                            var top = Math.Clamp(
                                current.Top + oy * moveY,
                                0,
                                Math.Max(0, 1 - height));

                            if (!CandidateInsideMap(
                                    left,
                                    top,
                                    width,
                                    height,
                                    rotation))
                                continue;

                            var score = Score(
                                screen,
                                map,
                                left,
                                top,
                                width,
                                height,
                                rotation,
                                pass >= 2 ? 28 : 24);

                            if (score > best.Score)
                            {
                                best = new Candidate(
                                    left,
                                    top,
                                    width,
                                    height,
                                    rotation,
                                    score);
                            }
                        }
                    }
                }
            }
        }

        return best;
    }

    private static double HeightFor(
        double width01,
        FeatureImage screen,
        FeatureImage map)
    {
        var screenAspect = screen.Height / (double)Math.Max(1, screen.Width);
        var mapAspectCorrection = map.Width / (double)Math.Max(1, map.Height);
        return width01 * screenAspect * mapAspectCorrection;
    }

    private static IReadOnlyList<double> Positions(double max, double step)
    {
        if (max <= 0.00001)
            return new[] { 0.0 };

        var result = new List<double>();

        for (var p = 0.0; p < max; p += step)
            result.Add(p);

        if (result.Count == 0 || Math.Abs(result[^1] - max) > 0.001)
            result.Add(max);

        return result;
    }

    private static double Score(
        FeatureImage screen,
        FeatureImage map,
        double left,
        double top,
        double width,
        double height,
        double rotationDeg,
        int grid = 20)
    {
        var centerX = left + width * 0.5;
        var centerY = top + height * 0.5;
        var radians = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var edgeCorrelation = new RunningCorrelation();
        var grayCorrelation = new RunningCorrelation();

        for (var gy = 1; gy < grid - 1; gy++)
        {
            var v = gy / (double)(grid - 1);
            var sy = (int)Math.Round(v * (screen.Height - 1));
            var localY = (v - 0.5) * height;

            for (var gx = 1; gx < grid - 1; gx++)
            {
                var u = gx / (double)(grid - 1);
                var sx = (int)Math.Round(u * (screen.Width - 1));
                var localX = (u - 0.5) * width;

                var mapX01 = centerX + localX * cos - localY * sin;
                var mapY01 = centerY + localX * sin + localY * cos;

                if (mapX01 < 0 || mapY01 < 0 || mapX01 > 1 || mapY01 > 1)
                    continue;

                var mx = mapX01 * (map.Width - 1);
                var my = mapY01 * (map.Height - 1);

                edgeCorrelation.Add(
                    screen.Edge[sy * screen.Width + sx],
                    SampleBilinear(map.Edge, map.Width, map.Height, mx, my));

                grayCorrelation.Add(
                    screen.Gray[sy * screen.Width + sx],
                    SampleBilinear(map.Gray, map.Width, map.Height, mx, my));
            }
        }

        var minimumSamples = Math.Max(
            20,
            (int)Math.Round((grid - 2) * (grid - 2) * 0.82));

        if (edgeCorrelation.Count < minimumSamples)
            return -1;

        var edge = edgeCorrelation.Correlation();
        var gray = grayCorrelation.Correlation();

        if (!double.IsFinite(edge))
            return -1;

        var combined = double.IsFinite(gray)
            ? edge * 0.78 + gray * 0.22
            : edge;

        // Only a tiny regularizer is used. Correctly rotated imagery must be
        // free to beat a visually similar north-up alias.
        var rotationPenalty =
            Math.Abs(NormalizeRotation(rotationDeg)) / 180.0 * 0.008;

        return Math.Clamp(combined - rotationPenalty, -1, 1);
    }

    private struct RunningCorrelation
    {
        public int Count { get; private set; }
        private double _sumA;
        private double _sumB;
        private double _sumAA;
        private double _sumBB;
        private double _sumAB;

        public void Add(double a, double b)
        {
            Count++;
            _sumA += a;
            _sumB += b;
            _sumAA += a * a;
            _sumBB += b * b;
            _sumAB += a * b;
        }

        public double Correlation()
        {
            if (Count < 4)
                return double.NaN;

            var covariance = _sumAB - _sumA * _sumB / Count;
            var varianceA = _sumAA - _sumA * _sumA / Count;
            var varianceB = _sumBB - _sumB * _sumB / Count;

            if (varianceA <= 1e-9 || varianceB <= 1e-9)
                return double.NaN;

            return Math.Clamp(
                covariance / Math.Sqrt(varianceA * varianceB),
                -1,
                1);
        }
    }

    private static double SampleBilinear(
        double[] data,
        int width,
        int height,
        double x,
        double y)
    {
        var x0 = Math.Clamp((int)Math.Floor(x), 0, width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, height - 1);
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var tx = Math.Clamp(x - x0, 0, 1);
        var ty = Math.Clamp(y - y0, 0, 1);

        var a = data[y0 * width + x0] * (1 - tx) +
                data[y0 * width + x1] * tx;

        var b = data[y1 * width + x0] * (1 - tx) +
                data[y1 * width + x1] * tx;

        return a * (1 - ty) + b * ty;
    }

    private static bool CandidateInsideMap(
        double left,
        double top,
        double width,
        double height,
        double rotationDeg)
    {
        var centerX = left + width * 0.5;
        var centerY = top + height * 0.5;
        var radians = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var corners = new[]
        {
            (-0.5, -0.5),
            (0.5, -0.5),
            (0.5, 0.5),
            (-0.5, 0.5)
        };

        foreach (var corner in corners)
        {
            var lx = corner.Item1 * width;
            var ly = corner.Item2 * height;
            var x = centerX + lx * cos - ly * sin;
            var y = centerY + lx * sin + ly * cos;

            if (x < -0.001 || y < -0.001 || x > 1.001 || y > 1.001)
                return false;
        }

        return true;
    }

    private static double RotationDistance(double a, double b) =>
        Math.Abs(NormalizeRotation(a - b));

    private static double NormalizeRotation(double degrees)
    {
        degrees %= 360;

        if (degrees > 180)
            degrees -= 360;
        if (degrees <= -180)
            degrees += 360;

        return degrees;
    }

    private static MapViewportRegistration ToRegistration(
        string mapId,
        Candidate best)
    {
        var confidence = Math.Clamp(
            (best.Score - 0.05) / 0.58,
            0,
            1);

        return new MapViewportRegistration
        {
            MapId = mapId,
            Left01 = best.Left,
            Top01 = best.Top,
            Width01 = best.Width,
            Height01 = best.Height,
            RotationDeg = best.RotationDeg,
            RawScore = best.Score,
            Confidence = confidence,
            RegisteredUtc = DateTime.UtcNow
        };
    }

    private readonly record struct Candidate(
        double Left,
        double Top,
        double Width,
        double Height,
        double RotationDeg,
        double Score);

    private sealed class FeatureImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public double[] Gray { get; init; } = Array.Empty<double>();
        public double[] Edge { get; init; } = Array.Empty<double>();

        public static FeatureImage FromBitmap(Bitmap bitmap, int maxDimension)
        {
            var scale = Math.Min(
                1.0,
                maxDimension / (double)Math.Max(bitmap.Width, bitmap.Height));

            var width = Math.Max(24, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(24, (int)Math.Round(bitmap.Height * scale));

            using var scaled = new Bitmap(width, height);

            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(bitmap, new Rectangle(0, 0, width, height));
            }

            var gray = new double[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var color = scaled.GetPixel(x, y);
                    gray[y * width + x] =
                        (color.R * 0.299 +
                         color.G * 0.587 +
                         color.B * 0.114) / 255.0;
                }
            }

            var edge = new double[width * height];

            for (var y = 1; y + 1 < height; y++)
            {
                for (var x = 1; x + 1 < width; x++)
                {
                    var gx = gray[y * width + x + 1] -
                             gray[y * width + x - 1];

                    var gy = gray[(y + 1) * width + x] -
                             gray[(y - 1) * width + x];

                    edge[y * width + x] = Math.Min(
                        1,
                        Math.Sqrt(gx * gx + gy * gy) * 1.7);
                }
            }

            return new FeatureImage
            {
                Width = width,
                Height = height,
                Gray = gray,
                Edge = edge
            };
        }
    }
}

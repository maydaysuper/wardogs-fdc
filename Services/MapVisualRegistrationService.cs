using System.Drawing;
using System.Drawing.Drawing2D;

namespace WardogsNavigator.Services;

public sealed class MapVisualRegistrationService
{
    private readonly MapAssetService _maps;
    private readonly Dictionary<string, FeatureImage> _baseFeatures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MapVisualRegistrationService(
        MapAssetService maps)
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
            var baseFeature =
                await GetBaseFeatureAsync(
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
        var baseFeature =
            FeatureImage.FromBitmap(
                baseMap,
                192);

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
        var screenFeature =
            FeatureImage.FromBitmap(
                screenshot,
                144);

        Candidate best =
            default;

        if (hint?.IsValid == true)
        {
            best = SearchNearHint(
                screenFeature,
                baseFeature,
                hint,
                cancellationToken);

            if (best.Score >= 0.38)
                return ToRegistration(
                    mapId,
                    best);
        }

        best = SearchGlobal(
            screenFeature,
            baseFeature,
            cancellationToken);

        if (best.Score <= -0.90)
            return null;

        best = Refine(
            screenFeature,
            baseFeature,
            best,
            cancellationToken);

        return ToRegistration(
            mapId,
            best);
    }

    private async Task<FeatureImage?> GetBaseFeatureAsync(
        string mapId,
        CancellationToken cancellationToken)
    {
        if (_baseFeatures.TryGetValue(
                mapId,
                out var cached))
            return cached;

        var bitmap =
            await _maps.GetBitmapAsync(
                mapId,
                cancellationToken);

        if (bitmap == null)
            return null;

        var feature =
            FeatureImage.FromBitmap(
                bitmap,
                192);

        _baseFeatures[mapId] =
            feature;

        return feature;
    }

    private static Candidate SearchNearHint(
        FeatureImage screen,
        FeatureImage map,
        MapViewportRegistration hint,
        CancellationToken ct)
    {
        var best =
            new Candidate
            {
                Score = -1
            };

        var widthFactors =
            new[]
            {
                0.90,
                0.95,
                1.00,
                1.05,
                1.10
            };

        foreach (var widthFactor in widthFactors)
        {
            ct.ThrowIfCancellationRequested();

            var width =
                Math.Clamp(
                    hint.Width01 *
                    widthFactor,
                    0.16,
                    1.0);

            var height =
                HeightFor(
                    width,
                    screen,
                    map);

            if (height > 1)
                continue;

            var stepX =
                Math.Max(
                    0.006,
                    width * 0.055);

            var stepY =
                Math.Max(
                    0.006,
                    height * 0.055);

            for (var oy = -2;
                 oy <= 2;
                 oy++)
            {
                for (var ox = -2;
                     ox <= 2;
                     ox++)
                {
                    var left =
                        Math.Clamp(
                            hint.Left01 +
                            ox * stepX,
                            0,
                            Math.Max(
                                0,
                                1 - width));

                    var top =
                        Math.Clamp(
                            hint.Top01 +
                            oy * stepY,
                            0,
                            Math.Max(
                                0,
                                1 - height));

                    var score =
                        Score(
                            screen,
                            map,
                            left,
                            top,
                            width,
                            height);

                    if (score >
                        best.Score)
                    {
                        best =
                            new Candidate
                            {
                                Left = left,
                                Top = top,
                                Width = width,
                                Height = height,
                                Score = score
                            };
                    }
                }
            }
        }

        return best;
    }

    private static Candidate SearchGlobal(
        FeatureImage screen,
        FeatureImage map,
        CancellationToken ct)
    {
        var best =
            new Candidate
            {
                Score = -1
            };

        var widths =
            new[]
            {
                1.00,
                0.90,
                0.80,
                0.70,
                0.61,
                0.53,
                0.46,
                0.39,
                0.33,
                0.28,
                0.24
            };

        foreach (var width in widths)
        {
            ct.ThrowIfCancellationRequested();

            var height =
                HeightFor(
                    width,
                    screen,
                    map);

            if (height > 1.001)
                continue;

            var stepX =
                Math.Max(
                    0.015,
                    width * 0.15);

            var stepY =
                Math.Max(
                    0.015,
                    height * 0.15);

            var maxLeft =
                Math.Max(
                    0,
                    1 - width);

            var maxTop =
                Math.Max(
                    0,
                    1 - height);

            var xs =
                Positions(
                    maxLeft,
                    stepX);

            var ys =
                Positions(
                    maxTop,
                    stepY);

            foreach (var top in ys)
            {
                foreach (var left in xs)
                {
                    var score =
                        Score(
                            screen,
                            map,
                            left,
                            top,
                            width,
                            height);

                    if (score >
                        best.Score)
                    {
                        best =
                            new Candidate
                            {
                                Left = left,
                                Top = top,
                                Width = width,
                                Height = height,
                                Score = score
                            };
                    }
                }
            }
        }

        return best;
    }

    private static Candidate Refine(
        FeatureImage screen,
        FeatureImage map,
        Candidate seed,
        CancellationToken ct)
    {
        var best = seed;

        for (var pass = 0;
             pass < 3;
             pass++)
        {
            ct.ThrowIfCancellationRequested();

            var scaleStep =
                pass switch
                {
                    0 => 0.045,
                    1 => 0.020,
                    _ => 0.008
                };

            var moveX =
                Math.Max(
                    0.002,
                    best.Width *
                    scaleStep);

            var moveY =
                Math.Max(
                    0.002,
                    best.Height *
                    scaleStep);

            var current = best;

            for (var sw = -1;
                 sw <= 1;
                 sw++)
            {
                var width =
                    Math.Clamp(
                        current.Width *
                        (
                            1 +
                            sw *
                            scaleStep
                        ),
                        0.15,
                        1);

                var height =
                    HeightFor(
                        width,
                        screen,
                        map);

                if (height > 1)
                    continue;

                for (var oy = -2;
                     oy <= 2;
                     oy++)
                {
                    for (var ox = -2;
                         ox <= 2;
                         ox++)
                    {
                        var left =
                            Math.Clamp(
                                current.Left +
                                ox * moveX,
                                0,
                                Math.Max(
                                    0,
                                    1 - width));

                        var top =
                            Math.Clamp(
                                current.Top +
                                oy * moveY,
                                0,
                                Math.Max(
                                    0,
                                    1 - height));

                        var score =
                            Score(
                                screen,
                                map,
                                left,
                                top,
                                width,
                                height,
                                24);

                        if (score >
                            best.Score)
                        {
                            best =
                                new Candidate
                                {
                                    Left = left,
                                    Top = top,
                                    Width = width,
                                    Height = height,
                                    Score = score
                                };
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
        var screenAspect =
            screen.Height /
            (double)Math.Max(
                1,
                screen.Width);

        var mapAspectCorrection =
            map.Width /
            (double)Math.Max(
                1,
                map.Height);

        return width01 *
               screenAspect *
               mapAspectCorrection;
    }

    private static IReadOnlyList<double> Positions(
        double max,
        double step)
    {
        if (max <= 0.00001)
            return new[]
            {
                0.0
            };

        var result =
            new List<double>();

        for (var p = 0.0;
             p < max;
             p += step)
            result.Add(p);

        if (result.Count == 0 ||
            Math.Abs(
                result[^1] -
                max) > 0.001)
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
        int grid = 20)
    {
        double sumA = 0;
        double sumB = 0;
        double sumAA = 0;
        double sumBB = 0;
        double sumAB = 0;
        var count = 0;

        for (var gy = 1;
             gy < grid - 1;
             gy++)
        {
            var v =
                gy /
                (double)(
                    grid - 1);

            var sy =
                (int)Math.Round(
                    v *
                    (screen.Height - 1));

            var mapY01 =
                top +
                v *
                height;

            var my =
                (int)Math.Round(
                    mapY01 *
                    (map.Height - 1));

            my = Math.Clamp(
                my,
                0,
                map.Height - 1);

            for (var gx = 1;
                 gx < grid - 1;
                 gx++)
            {
                var u =
                    gx /
                    (double)(
                        grid - 1);

                var sx =
                    (int)Math.Round(
                        u *
                        (screen.Width - 1));

                var mapX01 =
                    left +
                    u *
                    width;

                var mx =
                    (int)Math.Round(
                        mapX01 *
                        (map.Width - 1));

                mx = Math.Clamp(
                    mx,
                    0,
                    map.Width - 1);

                var a =
                    screen.Edge[
                        sy *
                        screen.Width +
                        sx];

                var b =
                    map.Edge[
                        my *
                        map.Width +
                        mx];

                sumA += a;
                sumB += b;
                sumAA += a * a;
                sumBB += b * b;
                sumAB += a * b;
                count++;
            }
        }

        if (count < 20)
            return -1;

        var cov =
            sumAB -
            sumA *
            sumB /
            count;

        var varA =
            sumAA -
            sumA *
            sumA /
            count;

        var varB =
            sumBB -
            sumB *
            sumB /
            count;

        if (varA <= 1e-8 ||
            varB <= 1e-8)
            return -1;

        return Math.Clamp(
            cov /
            Math.Sqrt(
                varA *
                varB),
            -1,
            1);
    }

    private static MapViewportRegistration ToRegistration(
        string mapId,
        Candidate best)
    {
        var confidence =
            Math.Clamp(
                (
                    best.Score -
                    0.05
                ) /
                0.58,
                0,
                1);

        return new MapViewportRegistration
        {
            MapId = mapId,
            Left01 = best.Left,
            Top01 = best.Top,
            Width01 = best.Width,
            Height01 = best.Height,
            RawScore = best.Score,
            Confidence = confidence,
            RegisteredUtc =
                DateTime.UtcNow
        };
    }

    private readonly record struct Candidate
    {
        public double Left { get; init; }
        public double Top { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Score { get; init; }
    }

    private sealed class FeatureImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public double[] Edge { get; init; } =
            Array.Empty<double>();

        public static FeatureImage FromBitmap(
            Bitmap bitmap,
            int maxDimension)
        {
            var scale =
                Math.Min(
                    1.0,
                    maxDimension /
                    (double)Math.Max(
                        bitmap.Width,
                        bitmap.Height));

            var width =
                Math.Max(
                    24,
                    (int)Math.Round(
                        bitmap.Width *
                        scale));

            var height =
                Math.Max(
                    24,
                    (int)Math.Round(
                        bitmap.Height *
                        scale));

            using var scaled =
                new Bitmap(
                    width,
                    height);

            using (var g =
                   Graphics.FromImage(scaled))
            {
                g.InterpolationMode =
                    InterpolationMode.HighQualityBilinear;

                g.DrawImage(
                    bitmap,
                    new Rectangle(
                        0,
                        0,
                        width,
                        height));
            }

            var gray =
                new double[
                    width *
                    height];

            for (var y = 0;
                 y < height;
                 y++)
            {
                for (var x = 0;
                     x < width;
                     x++)
                {
                    var c =
                        scaled.GetPixel(
                            x,
                            y);

                    gray[
                        y *
                        width +
                        x] =
                        (
                            c.R * 0.299 +
                            c.G * 0.587 +
                            c.B * 0.114
                        ) /
                        255.0;
                }
            }

            var edge =
                new double[
                    width *
                    height];

            for (var y = 1;
                 y + 1 < height;
                 y++)
            {
                for (var x = 1;
                     x + 1 < width;
                     x++)
                {
                    var gx =
                        gray[
                            y *
                            width +
                            x + 1] -
                        gray[
                            y *
                            width +
                            x - 1];

                    var gy =
                        gray[
                            (y + 1) *
                            width +
                            x] -
                        gray[
                            (y - 1) *
                            width +
                            x];

                    edge[
                        y *
                        width +
                        x] =
                        Math.Min(
                            1,
                            Math.Sqrt(
                                gx * gx +
                                gy * gy) *
                            1.7);
                }
            }

            return new FeatureImage
            {
                Width = width,
                Height = height,
                Edge = edge
            };
        }
    }
}

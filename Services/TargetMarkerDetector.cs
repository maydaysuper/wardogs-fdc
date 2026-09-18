using System.Drawing;
using System.Drawing.Drawing2D;

namespace WardogsNavigator.Services;

public sealed class TargetMarkerDetector
{
    public TargetMarkerProfile CreateProfile(
        Bitmap screenshot,
        Point selectedPixel)
    {
        var radius = 6;
        var samples =
            new List<ColorSample>();

        for (var y =
                 Math.Max(
                     0,
                     selectedPixel.Y -
                     radius);
             y <=
             Math.Min(
                 screenshot.Height - 1,
                 selectedPixel.Y +
                 radius);
             y++)
        {
            for (var x =
                     Math.Max(
                         0,
                         selectedPixel.X -
                         radius);
                 x <=
                 Math.Min(
                     screenshot.Width - 1,
                     selectedPixel.X +
                     radius);
                 x++)
            {
                var color =
                    screenshot.GetPixel(
                        x,
                        y);

                var hsv =
                    ToHsv(color);

                if (hsv.Saturation < 0.20 ||
                    hsv.Value < 0.20)
                    continue;

                samples.Add(
                    new ColorSample(
                        color,
                        hsv));
            }
        }

        if (samples.Count < 4)
            return new TargetMarkerProfile();

        // Prefer the most saturated pixels around the clicked marker so
        // background terrain does not dominate the profile.
        var selected =
            samples
                .OrderByDescending(
                    x =>
                        x.Hsv.Saturation *
                        x.Hsv.Value)
                .Take(
                    Math.Max(
                        4,
                        samples.Count /
                        2))
                .ToList();

        var sin = selected.Sum(
            x =>
                Math.Sin(
                    DegreesToRadians(
                        x.Hsv.HueDeg)) *
                x.Hsv.Saturation);

        var cos = selected.Sum(
            x =>
                Math.Cos(
                    DegreesToRadians(
                        x.Hsv.HueDeg)) *
                x.Hsv.Saturation);

        var hue =
            RadiansToDegrees(
                Math.Atan2(
                    sin,
                    cos));

        if (hue < 0)
            hue += 360;

        var saturation =
            selected.Average(
                x =>
                    x.Hsv.Saturation);

        var value =
            selected.Average(
                x =>
                    x.Hsv.Value);

        var hueSpread =
            selected
                .Select(
                    x =>
                        HueDistance(
                            hue,
                            x.Hsv.HueDeg))
                .OrderBy(x => x)
                .ToArray();

        var p85 =
            hueSpread[
                Math.Min(
                    hueSpread.Length - 1,
                    (int)Math.Round(
                        (
                            hueSpread.Length -
                            1
                        ) *
                        0.85))];

        return new TargetMarkerProfile
        {
            Enabled = true,
            HueDeg = hue,
            Saturation = saturation,
            Value = value,
            HueToleranceDeg =
                Math.Clamp(
                    p85 +
                    8,
                    12,
                    34),
            MinSaturation =
                Math.Clamp(
                    saturation *
                    0.52,
                    0.32,
                    0.78),
            MinValue =
                Math.Clamp(
                    value *
                    0.50,
                    0.30,
                    0.82),
            SampleR =
                (int)Math.Round(
                    selected.Average(
                        x => x.Color.R)),
            SampleG =
                (int)Math.Round(
                    selected.Average(
                        x => x.Color.G)),
            SampleB =
                (int)Math.Round(
                    selected.Average(
                        x => x.Color.B))
        };
    }

    public VisualTargetDetection Detect(
        Bitmap screenshot,
        MapViewportRegistration registration,
        TargetMarkerProfile profile,
        MapPoint? expectedWorld = null)
    {
        if (!registration.IsValid)
        {
            return new VisualTargetDetection
            {
                Error =
                    "地图视觉配准无效。"
            };
        }

        if (!profile.IsValid)
        {
            return new VisualTargetDetection
            {
                Error =
                    "尚未校准目标图标。"
            };
        }

        using var working =
            ResizeForDetection(
                screenshot,
                1200,
                out var scaleX,
                out var scaleY);

        var width =
            working.Width;
        var height =
            working.Height;

        var mask =
            new byte[
                width *
                height];

        var similarity =
            new float[
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
                var color =
                    working.GetPixel(
                        x,
                        y);

                var hsv =
                    ToHsv(color);

                if (hsv.Saturation <
                        profile.MinSaturation ||
                    hsv.Value <
                        profile.MinValue)
                    continue;

                var hueDistance =
                    HueDistance(
                        hsv.HueDeg,
                        profile.HueDeg);

                if (hueDistance >
                    profile.HueToleranceDeg)
                    continue;

                var rgbDistance =
                    Math.Sqrt(
                        Math.Pow(
                            color.R -
                            profile.SampleR,
                            2) +
                        Math.Pow(
                            color.G -
                            profile.SampleG,
                            2) +
                        Math.Pow(
                            color.B -
                            profile.SampleB,
                            2));

                if (rgbDistance > 165)
                    continue;

                var hueScore =
                    1.0 -
                    hueDistance /
                    Math.Max(
                        1,
                        profile.HueToleranceDeg);

                var saturationScore =
                    Math.Clamp(
                        hsv.Saturation /
                        Math.Max(
                            0.1,
                            profile.Saturation),
                        0,
                        1);

                var valueScore =
                    1.0 -
                    Math.Min(
                        1,
                        Math.Abs(
                            hsv.Value -
                            profile.Value) /
                        0.65);

                var score =
                    hueScore * 0.58 +
                    saturationScore * 0.22 +
                    valueScore * 0.20;

                if (score < 0.42)
                    continue;

                var index =
                    y *
                    width +
                    x;

                mask[index] = 1;
                similarity[index] =
                    (float)score;
            }
        }

        PointF? expectedPixel =
            null;

        if (expectedWorld is MapPoint expected)
        {
            var originalExpected =
                registration.WorldToScreenPixel(
                    expected,
                    screenshot.Width,
                    screenshot.Height);

            expectedPixel =
                new PointF(
                    originalExpected.X *
                    (float)scaleX,
                    originalExpected.Y *
                    (float)scaleY);
        }

        var visited =
            new byte[
                width *
                height];

        Component? best =
            null;

        var queue =
            new Queue<int>();

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var start =
                    y *
                    width +
                    x;

                if (mask[start] == 0 ||
                    visited[start] != 0)
                    continue;

                visited[start] = 1;
                queue.Enqueue(start);

                var count = 0;
                double weightedX = 0;
                double weightedY = 0;
                double weight = 0;
                double similaritySum = 0;

                var minX = x;
                var maxX = x;
                var minY = y;
                var maxY = y;

                while (queue.Count > 0)
                {
                    var index =
                        queue.Dequeue();

                    var px =
                        index %
                        width;

                    var py =
                        index /
                        width;

                    var pixelScore =
                        Math.Max(
                            0.01,
                            similarity[index]);

                    count++;
                    weightedX +=
                        px *
                        pixelScore;
                    weightedY +=
                        py *
                        pixelScore;
                    weight +=
                        pixelScore;
                    similaritySum +=
                        pixelScore;

                    minX =
                        Math.Min(
                            minX,
                            px);
                    maxX =
                        Math.Max(
                            maxX,
                            px);
                    minY =
                        Math.Min(
                            minY,
                            py);
                    maxY =
                        Math.Max(
                            maxY,
                            py);

                    for (var oy = -1;
                         oy <= 1;
                         oy++)
                    {
                        for (var ox = -1;
                             ox <= 1;
                             ox++)
                        {
                            if (ox == 0 &&
                                oy == 0)
                                continue;

                            var nx =
                                px +
                                ox;

                            var ny =
                                py +
                                oy;

                            if (nx < 0 ||
                                ny < 0 ||
                                nx >= width ||
                                ny >= height)
                                continue;

                            var ni =
                                ny *
                                width +
                                nx;

                            if (mask[ni] == 0 ||
                                visited[ni] != 0)
                                continue;

                            visited[ni] = 1;
                            queue.Enqueue(ni);
                        }
                    }
                }

                if (count < 3 ||
                    weight <= 0)
                    continue;

                var boxWidth =
                    maxX -
                    minX +
                    1;

                var boxHeight =
                    maxY -
                    minY +
                    1;

                if (boxWidth > 110 ||
                    boxHeight > 110)
                    continue;

                var cx =
                    weightedX /
                    weight;

                var cy =
                    weightedY /
                    weight;

                var averageSimilarity =
                    similaritySum /
                    count;

                var compactness =
                    count /
                    (double)Math.Max(
                        1,
                        boxWidth *
                        boxHeight);

                var areaScore =
                    Math.Clamp(
                        count /
                        28.0,
                        0,
                        1);

                var compactScore =
                    Math.Clamp(
                        compactness /
                        0.42,
                        0,
                        1);

                var proximityScore =
                    0.5;

                if (expectedPixel is PointF ep)
                {
                    var distance =
                        Math.Sqrt(
                            Math.Pow(
                                cx -
                                ep.X,
                                2) +
                            Math.Pow(
                                cy -
                                ep.Y,
                                2));

                    var diagonal =
                        Math.Sqrt(
                            width *
                            width +
                            height *
                            height);

                    proximityScore =
                        Math.Clamp(
                            1.0 -
                            distance /
                            Math.Max(
                                1,
                                diagonal *
                                0.35),
                            0,
                            1);
                }

                var componentScore =
                    averageSimilarity *
                    0.58 +
                    areaScore *
                    0.15 +
                    compactScore *
                    0.12 +
                    proximityScore *
                    0.15;

                if (best == null ||
                    componentScore >
                    best.Score)
                {
                    best =
                        new Component
                        {
                            X = cx,
                            Y = cy,
                            PixelCount = count,
                            Score =
                                componentScore
                        };
                }
            }
        }

        if (best == null)
        {
            return new VisualTargetDetection
            {
                Error =
                    "未在已配准游戏地图中找到目标图标。",
                RegistrationConfidence =
                    registration.Confidence
            };
        }

        var originalX =
            best.X /
            scaleX;

        var originalY =
            best.Y /
            scaleY;

        var point =
            registration.ScreenPixelToWorld(
                originalX,
                originalY,
                screenshot.Width,
                screenshot.Height);

        var confidence =
            Math.Clamp(
                best.Score *
                (
                    0.55 +
                    registration.Confidence *
                    0.45
                ),
                0,
                1);

        return new VisualTargetDetection
        {
            Success =
                point.IsInsideMap &&
                confidence >= 0.42,
            Point = point,
            PixelX = originalX,
            PixelY = originalY,
            Confidence = confidence,
            RegistrationConfidence =
                registration.Confidence,
            Source = "visual-marker",
            Error =
                confidence >= 0.42
                    ? ""
                    : "找到候选目标图标，但综合置信度不足。"
        };
    }

    private static Bitmap ResizeForDetection(
        Bitmap source,
        int maxDimension,
        out double scaleX,
        out double scaleY)
    {
        var max =
            Math.Max(
                source.Width,
                source.Height);

        if (max <= maxDimension)
        {
            scaleX = 1;
            scaleY = 1;
            return new Bitmap(source);
        }

        var scale =
            maxDimension /
            (double)max;

        var width =
            Math.Max(
                1,
                (int)Math.Round(
                    source.Width *
                    scale));

        var height =
            Math.Max(
                1,
                (int)Math.Round(
                    source.Height *
                    scale));

        var result =
            new Bitmap(
                width,
                height);

        using (var g =
               Graphics.FromImage(result))
        {
            g.InterpolationMode =
                InterpolationMode.HighQualityBilinear;

            g.DrawImage(
                source,
                new Rectangle(
                    0,
                    0,
                    width,
                    height));
        }

        scaleX =
            width /
            (double)source.Width;

        scaleY =
            height /
            (double)source.Height;

        return result;
    }

    private static Hsv ToHsv(
        Color color)
    {
        var r =
            color.R /
            255.0;
        var g =
            color.G /
            255.0;
        var b =
            color.B /
            255.0;

        var max =
            Math.Max(
                r,
                Math.Max(
                    g,
                    b));

        var min =
            Math.Min(
                r,
                Math.Min(
                    g,
                    b));

        var delta =
            max -
            min;

        double hue;

        if (delta <= 1e-9)
        {
            hue = 0;
        }
        else if (Math.Abs(
                     max -
                     r) <=
                 1e-9)
        {
            hue =
                60 *
                (
                    (
                        g -
                        b
                    ) /
                    delta %
                    6
                );
        }
        else if (Math.Abs(
                     max -
                     g) <=
                 1e-9)
        {
            hue =
                60 *
                (
                    (
                        b -
                        r
                    ) /
                    delta +
                    2
                );
        }
        else
        {
            hue =
                60 *
                (
                    (
                        r -
                        g
                    ) /
                    delta +
                    4
                );
        }

        if (hue < 0)
            hue += 360;

        var saturation =
            max <= 1e-9
                ? 0
                : delta /
                  max;

        return new Hsv(
            hue,
            saturation,
            max);
    }

    private static double HueDistance(
        double a,
        double b)
    {
        var d =
            Math.Abs(
                a -
                b) %
            360;

        return d > 180
            ? 360 -
              d
            : d;
    }

    private static double DegreesToRadians(
        double degrees) =>
        degrees *
        Math.PI /
        180.0;

    private static double RadiansToDegrees(
        double radians) =>
        radians *
        180.0 /
        Math.PI;

    private readonly record struct Hsv(
        double HueDeg,
        double Saturation,
        double Value);

    private readonly record struct ColorSample(
        Color Color,
        Hsv Hsv);

    private sealed class Component
    {
        public double X { get; init; }
        public double Y { get; init; }
        public int PixelCount { get; init; }
        public double Score { get; init; }
    }
}

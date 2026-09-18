using System.Drawing;

namespace WardogsNavigator;

public sealed class MapViewportRegistration
{
    public string MapId { get; set; } = "";
    public double Left01 { get; set; }
    public double Top01 { get; set; }
    public double Width01 { get; set; } = 1;
    public double Height01 { get; set; } = 1;
    public double RotationDeg { get; set; }
    public double Confidence { get; set; }
    public double RawScore { get; set; }
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;

    public bool IsValid =>
        Width01 > 0.02 &&
        Height01 > 0.02 &&
        Left01 >= -0.001 &&
        Top01 >= -0.001 &&
        Left01 + Width01 <= 1.001 &&
        Top01 + Height01 <= 1.001;

    public MapPoint ScreenPixelToWorld(
        double x,
        double y,
        int screenWidth,
        int screenHeight)
    {
        var u = Math.Clamp(
            x / Math.Max(1.0, screenWidth - 1.0),
            0,
            1);

        var v = Math.Clamp(
            y / Math.Max(1.0, screenHeight - 1.0),
            0,
            1);

        var centerX =
            Left01 +
            Width01 * 0.5;

        var centerY =
            Top01 +
            Height01 * 0.5;

        var localX =
            (u - 0.5) *
            Width01;

        var localY =
            (v - 0.5) *
            Height01;

        var radians =
            RotationDeg *
            Math.PI /
            180.0;

        var cos =
            Math.Cos(radians);

        var sin =
            Math.Sin(radians);

        var mapX01 =
            centerX +
            localX * cos -
            localY * sin;

        var mapY01 =
            centerY +
            localX * sin +
            localY * cos;

        return new MapPoint(
            Math.Clamp(
                mapX01,
                0,
                1) *
            MapPoint.MapSize,
            (
                1.0 -
                Math.Clamp(
                    mapY01,
                    0,
                    1)
            ) *
            MapPoint.MapSize);
    }

    public PointF MapNormalizedToScreen01(
        double mapX01,
        double mapY01)
    {
        var centerX =
            Left01 +
            Width01 * 0.5;

        var centerY =
            Top01 +
            Height01 * 0.5;

        var dx =
            mapX01 -
            centerX;

        var dy =
            mapY01 -
            centerY;

        var radians =
            -RotationDeg *
            Math.PI /
            180.0;

        var cos =
            Math.Cos(radians);
        var sin =
            Math.Sin(radians);

        return new PointF(
            (float)(
                0.5 +
                (
                    dx * cos -
                    dy * sin
                ) /
                Math.Max(
                    0.0001,
                    Width01)),
            (float)(
                0.5 +
                (
                    dx * sin +
                    dy * cos
                ) /
                Math.Max(
                    0.0001,
                    Height01)));
    }

    public PointF[] GetMapNormalizedCorners()
    {
        var centerX =
            Left01 +
            Width01 * 0.5;

        var centerY =
            Top01 +
            Height01 * 0.5;

        var radians =
            RotationDeg *
            Math.PI /
            180.0;

        var cos =
            Math.Cos(radians);
        var sin =
            Math.Sin(radians);

        var corners =
            new[]
            {
                (-0.5, -0.5),
                (0.5, -0.5),
                (0.5, 0.5),
                (-0.5, 0.5)
            };

        return corners
            .Select(c =>
            {
                var lx =
                    c.Item1 *
                    Width01;
                var ly =
                    c.Item2 *
                    Height01;

                return new PointF(
                    (float)(
                        centerX +
                        lx * cos -
                        ly * sin),
                    (float)(
                        centerY +
                        lx * sin +
                        ly * cos));
            })
            .ToArray();
    }

    public bool RotatedViewportInsideMap(double margin = 0.001)
    {
        return GetMapNormalizedCorners()
            .All(p =>
                p.X >= -margin &&
                p.Y >= -margin &&
                p.X <= 1 + margin &&
                p.Y <= 1 + margin);
    }

    public PointF WorldToScreenPixel(
        MapPoint point,
        int screenWidth,
        int screenHeight)
    {
        var x01 =
            point.X /
            MapPoint.MapSize;

        var y01 =
            1.0 -
            point.Y /
            MapPoint.MapSize;

        var centerX =
            Left01 +
            Width01 * 0.5;

        var centerY =
            Top01 +
            Height01 * 0.5;

        var dx =
            x01 -
            centerX;

        var dy =
            y01 -
            centerY;

        var radians =
            -RotationDeg *
            Math.PI /
            180.0;

        var cos =
            Math.Cos(radians);

        var sin =
            Math.Sin(radians);

        var localX =
            dx * cos -
            dy * sin;

        var localY =
            dx * sin +
            dy * cos;

        var u =
            0.5 +
            localX /
            Math.Max(
                0.0001,
                Width01);

        var v =
            0.5 +
            localY /
            Math.Max(
                0.0001,
                Height01);

        return new PointF(
            (float)(
                u *
                Math.Max(
                    1,
                    screenWidth - 1)),
            (float)(
                v *
                Math.Max(
                    1,
                    screenHeight - 1)));
    }
}

public sealed class TargetMarkerProfile
{
    public bool Enabled { get; set; }
    public double HueDeg { get; set; }
    public double Saturation { get; set; }
    public double Value { get; set; }
    public double HueToleranceDeg { get; set; } = 20;
    public double MinSaturation { get; set; } = 0.45;
    public double MinValue { get; set; } = 0.45;
    public int SampleR { get; set; }
    public int SampleG { get; set; }
    public int SampleB { get; set; }

    public bool IsValid =>
        Enabled &&
        HueDeg >= 0 &&
        HueDeg < 360 &&
        Saturation >= 0.15 &&
        Value >= 0.15;
}

public sealed class VisualTargetDetection
{
    public bool Success { get; set; }
    public MapPoint Point { get; set; }
    public double PixelX { get; set; }
    public double PixelY { get; set; }
    public double Confidence { get; set; }
    public double RegistrationConfidence { get; set; }
    public string Source { get; set; } = "";
    public string Error { get; set; } = "";
}

public sealed class MapVisualMemory
{
    public string MapId { get; set; } = "";
    public MapViewportRegistration? LastRegistration { get; set; }
    public int SuccessfulRegistrations { get; set; }
    public int FailedRegistrations { get; set; }
    public double RegistrationConfidenceEwma { get; set; }
    public MapPoint? LastVisualTarget { get; set; }
    public double LastTargetConfidence { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WardogsNavigator.Services;

/// <summary>
/// Lightweight local frame-to-frame motion estimator.
/// It never reads game memory and never sends frames to a network service.
/// The estimator downsamples the calibrated road view and searches for the
/// best global image translation. Residual frame change is also kept because
/// forward vehicle motion often produces radial expansion rather than a pure
/// translation.
/// </summary>
public sealed class VisualMotionEstimator
{
    private const int SampleWidth = 64;
    private const int SampleHeight = 36;
    private const int MaxShiftX = 5;
    private const int MaxShiftY = 3;

    private byte[]? _previous;
    private DateTime _previousUtc;

    public void Reset()
    {
        _previous = null;
        _previousUtc = default;
    }

    public VisualMotionSample Analyze(
        Bitmap frame,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var current = ToGray(frame);
        if (_previous == null)
        {
            _previous = current;
            _previousUtc = nowUtc;
            return new VisualMotionSample
            {
                TimestampUtc = nowUtc
            };
        }

        var dt = Math.Clamp(
            (nowUtc - _previousUtc).TotalSeconds,
            0.02,
            2.5);

        var baseline = MeanAbsoluteDifference(
            current,
            _previous,
            0,
            0);

        var bestError = double.MaxValue;
        var bestDx = 0;
        var bestDy = 0;

        for (var dy = -MaxShiftY; dy <= MaxShiftY; dy++)
        {
            for (var dx = -MaxShiftX; dx <= MaxShiftX; dx++)
            {
                var error = MeanAbsoluteDifference(
                    current,
                    _previous,
                    dx,
                    dy);

                if (error < bestError)
                {
                    bestError = error;
                    bestDx = dx;
                    bestDy = dy;
                }
            }
        }

        var texture = TextureScore(current);
        var improvement = baseline <= 0.5
            ? 0
            : Math.Clamp(
                (baseline - bestError) / baseline,
                0,
                1);

        var translation =
            Math.Sqrt(
                bestDx * bestDx +
                bestDy * bestDy);

        // Forward travel is not a global translation. Use raw change plus
        // the best translation magnitude as a motion-energy proxy.
        var motion01 = Math.Clamp(
            baseline / 34.0 * 0.78 +
            translation / 6.0 * 0.22,
            0,
            1);

        var confidence = Math.Clamp(
            texture / 22.0 * 0.58 +
            improvement * 0.42,
            0,
            1);

        // For a forward-facing camera, horizontal image translation is a
        // useful low-cost yaw cue. It is deliberately conservative because
        // map matching remains the primary drift correction layer.
        var yawDeltaDeg = Math.Clamp(
            -bestDx * 1.35,
            -9,
            9);

        var result = new VisualMotionSample
        {
            TimestampUtc = nowUtc,
            DeltaSeconds = dt,
            ShiftXPixels = bestDx,
            ShiftYPixels = bestDy,
            YawDeltaDeg = yawDeltaDeg,
            Motion01 = motion01,
            Confidence = confidence,
            Residual01 = Math.Clamp(
                bestError / 34.0,
                0,
                1),
            IsMoving =
                motion01 >= 0.055 &&
                (confidence >= 0.10 || baseline >= 5.5)
        };

        _previous = current;
        _previousUtc = nowUtc;
        return result;
    }

    private static byte[] ToGray(Bitmap source)
    {
        using var scaled = new Bitmap(
            SampleWidth,
            SampleHeight,
            PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(scaled))
        {
            g.DrawImage(
                source,
                new Rectangle(
                    0,
                    0,
                    SampleWidth,
                    SampleHeight));
        }

        var bytes =
            new byte[
                SampleWidth *
                SampleHeight];

        var rect =
            new Rectangle(
                0,
                0,
                SampleWidth,
                SampleHeight);

        var data =
            scaled.LockBits(
                rect,
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

        try
        {
            var row =
                new byte[
                    Math.Abs(
                        data.Stride)];

            for (var y = 0; y < SampleHeight; y++)
            {
                var rowPtr =
                    IntPtr.Add(
                        data.Scan0,
                        y * data.Stride);

                Marshal.Copy(
                    rowPtr,
                    row,
                    0,
                    row.Length);

                for (var x = 0; x < SampleWidth; x++)
                {
                    var i = x * 3;
                    var b = row[i];
                    var g = row[i + 1];
                    var r = row[i + 2];

                    bytes[
                        y * SampleWidth +
                        x] =
                        (byte)(
                            (
                                r * 30 +
                                g * 59 +
                                b * 11
                            ) /
                            100);
                }
            }
        }
        finally
        {
            scaled.UnlockBits(data);
        }

        return bytes;
    }

    private static double MeanAbsoluteDifference(
        byte[] current,
        byte[] previous,
        int dx,
        int dy)
    {
        var marginX =
            MaxShiftX + 2;
        var marginY =
            MaxShiftY + 2;

        long total = 0;
        var count = 0;

        for (var y = marginY;
             y < SampleHeight - marginY;
             y++)
        {
            var py = y + dy;
            if (py < 0 ||
                py >= SampleHeight)
                continue;

            for (var x = marginX;
                 x < SampleWidth - marginX;
                 x++)
            {
                var px = x + dx;
                if (px < 0 ||
                    px >= SampleWidth)
                    continue;

                total += Math.Abs(
                    current[
                        y * SampleWidth +
                        x] -
                    previous[
                        py * SampleWidth +
                        px]);

                count++;
            }
        }

        return count == 0
            ? 255
            : total / (double)count;
    }

    private static double TextureScore(
        byte[] gray)
    {
        long total = 0;
        var count = 0;

        for (var y = 1;
             y < SampleHeight - 1;
             y += 2)
        {
            for (var x = 1;
                 x < SampleWidth - 1;
                 x += 2)
            {
                var center =
                    gray[
                        y * SampleWidth +
                        x];

                total += Math.Abs(
                    center -
                    gray[
                        y * SampleWidth +
                        x + 1]);

                total += Math.Abs(
                    center -
                    gray[
                        (y + 1) *
                        SampleWidth +
                        x]);

                count += 2;
            }
        }

        return count == 0
            ? 0
            : total / (double)count;
    }
}

public sealed class VisualMotionSample
{
    public DateTime TimestampUtc { get; set; }
    public double DeltaSeconds { get; set; }
    public int ShiftXPixels { get; set; }
    public int ShiftYPixels { get; set; }
    public double YawDeltaDeg { get; set; }
    public double Motion01 { get; set; }
    public double Residual01 { get; set; }
    public double Confidence { get; set; }
    public bool IsMoving { get; set; }
}

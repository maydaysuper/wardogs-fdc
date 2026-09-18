using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WardogsNavigator.Services;

public static class AiVisionCachePolicy
{
    public static string BuildRouteKey(
        string mapId,
        IEnumerable<string> edgeIds) =>
        mapId + "|" + string.Join(
            ",",
            edgeIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    public static bool IsHit(
        string cachedRouteKey,
        ulong cachedVisualHash,
        DateTime cachedAtUtc,
        string routeKey,
        ulong visualHash,
        DateTime nowUtc,
        int cacheSeconds)
    {
        var ttl = TimeSpan.FromSeconds(
            Math.Clamp(cacheSeconds, 10, 600));

        return nowUtc - cachedAtUtc <= ttl &&
               cachedRouteKey.Equals(routeKey, StringComparison.Ordinal) &&
               HammingDistance(cachedVisualHash, visualHash) <= 4;
    }

    public static ulong ComputeDHash(Bitmap source)
    {
        using var small = new Bitmap(
            9,
            8,
            PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(
                source,
                new Rectangle(0, 0, 9, 8));
        }

        ulong hash = 0;
        var bit = 0;

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var a = small.GetPixel(x, y);
                var b = small.GetPixel(x + 1, y);

                var la = a.R * 3 + a.G * 6 + a.B;
                var lb = b.R * 3 + b.G * 6 + b.B;

                if (la > lb)
                    hash |= 1UL << bit;

                bit++;
            }
        }

        return hash;
    }

    public static int HammingDistance(ulong a, ulong b)
    {
        var value = a ^ b;
        var count = 0;

        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }
}

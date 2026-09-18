using System.Drawing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace WardogsNavigator.Services;

public sealed class MapAssetService : IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly string _cacheDir;
    private readonly Dictionary<string, Bitmap> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public MapAssetService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator", "maps");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<string> GetMapWebpPathAsync(string mapId, CancellationToken cancellationToken = default)
    {
        mapId = Normalize(mapId);
        var path = Path.Combine(_cacheDir, mapId + ".webp");
        if (File.Exists(path) && new FileInfo(path).Length > 1000) return path;

        var url = "https://raw.githubusercontent.com/maydaysuper/wardogs-fdc-web/main/public/maps/" + mapId + ".webp";
        var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    public async Task<Bitmap?> GetBitmapAsync(string mapId, CancellationToken cancellationToken = default)
    {
        mapId = Normalize(mapId);
        if (_bitmaps.TryGetValue(mapId, out var cached)) return cached;

        try
        {
            var path = await GetMapWebpPathAsync(mapId, cancellationToken);
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            ms.Position = 0;
            using var temp = new Bitmap(ms);
            var bmp = new Bitmap(temp);
            _bitmaps[mapId] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
    }

    private static string Normalize(string id) => id.ToLowerInvariant() switch
    {
        "bakurani" => "bakurani",
        "ozeti" => "ozeti",
        "zestafona" => "zestafona",
        _ => "bakurani"
    };
}

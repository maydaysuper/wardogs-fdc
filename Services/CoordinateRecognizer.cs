using System.Drawing;
using System.Text.RegularExpressions;
using Tesseract;

namespace WardogsNavigator.Services;

public sealed partial class CoordinateRecognizer : IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly string _tessDir;
    private TesseractEngine? _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CoordinateRecognizer()
    {
        _tessDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WardogsNavigator", "tessdata");
        Directory.CreateDirectory(_tessDir);
    }

    public async Task<OcrCoordinateResult> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureEngineAsync(cancellationToken);
            using var pix = PixConverter.ToPix(bitmap);
            using var page = _engine!.Process(pix, PageSegMode.Auto);
            var text = page.GetText() ?? "";
            if (TryParseText(text, out var point))
                return new OcrCoordinateResult { Success = true, Point = point, RawText = text.Trim() };
            return new OcrCoordinateResult { RawText = text.Trim(), Error = "OCR 已完成，但未解析到有效 X/Y。" };
        }
        catch (Exception ex)
        {
            return new OcrCoordinateResult { Error = ex.Message };
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool TryParseText(string text, out MapPoint point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var named = NamedRegex().Match(text);
        if (named.Success &&
            TryNum(named.Groups[1].Value, out var x1) &&
            TryNum(named.Groups[2].Value, out var y1))
        {
            point = new MapPoint(x1, y1);
            return point.IsInsideMap;
        }

        var nums = NumberRegex().Matches(text)
            .Select(m => m.Value)
            .Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? (double?)n : null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        for (var i = 0; i + 1 < nums.Count; i++)
        {
            var p = new MapPoint(nums[i], nums[i + 1]);
            if (p.IsInsideMap)
            {
                point = p;
                return true;
            }
        }
        return false;
    }

    private async Task EnsureEngineAsync(CancellationToken ct)
    {
        if (_engine != null) return;
        var trained = Path.Combine(_tessDir, "eng.traineddata");
        if (!File.Exists(trained) || new FileInfo(trained).Length < 100_000)
        {
            var url = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata";
            var bytes = await Http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(trained, bytes, ct);
        }

        _engine = new TesseractEngine(_tessDir, "eng", EngineMode.Default);
        _engine.SetVariable("tessedit_char_whitelist", "0123456789xXyY:,.=- /");
        _engine.SetVariable("user_defined_dpi", "160");
    }

    private static bool TryNum(string s, out double n) =>
        double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out n);

    [GeneratedRegex(@"[xX]\s*[:=]?\s*(-?\d{1,3}(?:[\.,]\d+)?)\D{0,16}[yY]\s*[:=]?\s*(-?\d{1,3}(?:[\.,]\d+)?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NamedRegex();

    [GeneratedRegex(@"-?\d{1,3}(?:\.\d+)?")]
    private static partial Regex NumberRegex();

    public void Dispose()
    {
        _engine?.Dispose();
        _gate.Dispose();
    }
}

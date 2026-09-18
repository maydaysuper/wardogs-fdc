using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WardogsNavigator.Services;

public sealed class AiVisionNavigationService
{
    private readonly DeepSeekClient _deepSeek;
    private readonly MapAssetService _maps;

    public AiVisionNavigationService(
        DeepSeekClient deepSeek,
        MapAssetService maps)
    {
        _deepSeek = deepSeek;
        _maps = maps;
    }

    public async Task<AiVisionNavigationReport> AnalyzeRouteAsync(
        string apiKey,
        string mapId,
        RoadGraph graph,
        RoutePlan route,
        Bitmap gameMapScreenshot,
        MapPoint? current = null,
        MapPoint? target = null,
        CancellationToken cancellationToken = default)
    {
        if (route.EdgeIds.Count == 0)
        {
            return new AiVisionNavigationReport
            {
                Summary =
                    "当前路线没有 Road Graph edge，无法进行可验证的视觉路段分析。",
                Findings = new List<AiVisionFinding>()
            };
        }

        var validEdges = graph.Edges
            .Where(e => route.EdgeIds.Contains(
                e.Id,
                StringComparer.OrdinalIgnoreCase))
            .Take(24)
            .ToList();

        if (validEdges.Count == 0)
        {
            return new AiVisionNavigationReport
            {
                Summary =
                    "当前路线 edge 与当前地图 Road Graph 不匹配。",
                Findings = new List<AiVisionFinding>()
            };
        }

        var tokenToEdge = validEdges
            .Select((edge, index) => new
            {
                Token = "R" + (index + 1).ToString("D2"),
                edge.Id
            })
            .ToDictionary(
                x => x.Token,
                x => x.Id,
                StringComparer.OrdinalIgnoreCase);

        using var reference = await BuildReferenceAsync(
            mapId,
            graph,
            validEdges,
            tokenToEdge,
            current,
            target,
            cancellationToken);

        var images = new[]
        {
            ToPngScaled(
                gameMapScreenshot,
                1600),
            ToPng(reference)
        };

        var candidates = tokenToEdge
            .Select(x => x.Key + "=" + x.Value)
            .ToArray();

        var system =
            "你是 WARDOGS 地图视觉导航校验器。输入包含两张图片：" +
            "第一张是用户游戏窗口中截取的地图区域；第二张是程序根据当前地图和 Road Graph 生成的参考图。" +
            "参考图中当前路线候选路段只用 R01、R02 等 token 标注。" +
            "你不能创造任何新的路段、坐标或敌情。只能判断候选 token 在第一张游戏地图截图中是否出现" +
            "明显的道路中断、封闭/障碍、危险标记、路线不一致，或者证据不足。" +
            "普通 UI 图标、文字遮挡、地图样式差异不能自动视为封路。" +
            "如果两张图无法可靠对齐，必须返回 uncertain 并降低 confidence。" +
            "输出必须是 JSON。kind 只能是 blocked、danger、uncertain、clear。" +
            "severity 和 confidence 范围都是 0 到 1。" +
            "JSON 示例：{\"summary\":\"...\",\"findings\":[{" +
            "\"edgeToken\":\"R03\",\"kind\":\"blocked\",\"severity\":0.9," +
            "\"confidence\":0.86,\"reason\":\"...\"}]}";

        var user =
            "当前地图：" + mapId + "\n" +
            "候选路段 token：" + string.Join(", ", candidates) + "\n" +
            "请比较两张图片。第一张是游戏截图，第二张是带 token 的程序参考图。" +
            "只返回有意义的候选判断；没有异常的路段可以省略，整体无异常时 findings 为空。" +
            "输出 JSON。";

        var raw = await _deepSeek.AskJsonWithImagesAsync<RawVisionReport>(
            apiKey,
            "deepseek-flash",
            system,
            user,
            images,
            cancellationToken);

        raw.Findings ??= new List<RawVisionFinding>();

        var findings = new List<AiVisionFinding>();

        foreach (var finding in raw.Findings)
        {
            if (!tokenToEdge.TryGetValue(
                    finding.EdgeToken ?? "",
                    out var edgeId))
                continue;

            var kind = NormalizeKind(finding.Kind);

            findings.Add(new AiVisionFinding
            {
                EdgeId = edgeId,
                Kind = kind,
                Severity = Math.Clamp(finding.Severity, 0, 1),
                Confidence = Math.Clamp(finding.Confidence, 0, 1),
                Reason = finding.Reason ?? ""
            });
        }

        return new AiVisionNavigationReport
        {
            Summary = raw.Summary ?? "",
            Findings = findings
        };
    }

    private async Task<Bitmap> BuildReferenceAsync(
        string mapId,
        RoadGraph graph,
        IReadOnlyList<RoadEdge> routeEdges,
        IReadOnlyDictionary<string, string> tokenToEdge,
        MapPoint? current,
        MapPoint? target,
        CancellationToken cancellationToken)
    {
        var baseMap = await _maps.GetBitmapAsync(
            mapId,
            cancellationToken);

        const int size = 1024;
        var output = new Bitmap(
            size,
            size,
            PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(output);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode =
            InterpolationMode.HighQualityBicubic;

        if (baseMap != null)
        {
            g.DrawImage(
                baseMap,
                new Rectangle(0, 0, size, size));
        }
        else
        {
            g.Clear(Color.FromArgb(25, 28, 34));
        }

        using (var veil = new SolidBrush(
                   Color.FromArgb(62, 0, 0, 0)))
        {
            g.FillRectangle(
                veil,
                0,
                0,
                size,
                size);
        }

        var nodes = graph.Nodes.ToDictionary(
            n => n.Id,
            StringComparer.OrdinalIgnoreCase);

        var edgeToToken = tokenToEdge
            .ToDictionary(
                x => x.Value,
                x => x.Key,
                StringComparer.OrdinalIgnoreCase);

        using var routeShadow = new Pen(
            Color.FromArgb(210, 0, 0, 0),
            13)
        {
            LineJoin = LineJoin.Round
        };

        using var routePen = new Pen(
            Color.Lime,
            7)
        {
            LineJoin = LineJoin.Round
        };

        using var labelFont = new Font(
            "Segoe UI",
            16,
            FontStyle.Bold,
            GraphicsUnit.Pixel);

        foreach (var edge in routeEdges)
        {
            if (!nodes.TryGetValue(edge.A, out var a) ||
                !nodes.TryGetValue(edge.B, out var b) ||
                !edgeToToken.TryGetValue(edge.Id, out var token))
                continue;

            var p1 = ToPixel(a.Position, size);
            var p2 = ToPixel(b.Position, size);

            g.DrawLine(routeShadow, p1, p2);
            g.DrawLine(routePen, p1, p2);

            var mid = new Point(
                (p1.X + p2.X) / 2,
                (p1.Y + p2.Y) / 2);

            var textSize = TextRenderer.MeasureText(
                token,
                labelFont);

            var labelRect = new Rectangle(
                mid.X - textSize.Width / 2 - 5,
                mid.Y - textSize.Height / 2 - 3,
                textSize.Width + 10,
                textSize.Height + 6);

            using var bg = new SolidBrush(
                Color.FromArgb(230, 10, 10, 10));
            using var border = new Pen(
                Color.Yellow,
                2);

            g.FillRectangle(bg, labelRect);
            g.DrawRectangle(border, labelRect);

            TextRenderer.DrawText(
                g,
                token,
                labelFont,
                labelRect,
                Color.Yellow,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter);
        }

        if (current is MapPoint self)
            DrawPoint(g, self, size, Color.DeepSkyBlue, "YOU");

        if (target is MapPoint tgt)
            DrawPoint(g, tgt, size, Color.OrangeRed, "TARGET");

        return output;
    }

    private static void DrawPoint(
        Graphics g,
        MapPoint point,
        int size,
        Color color,
        string label)
    {
        var p = ToPixel(point, size);

        using var brush = new SolidBrush(color);
        using var outline = new Pen(Color.Black, 4);

        g.FillEllipse(
            brush,
            p.X - 9,
            p.Y - 9,
            18,
            18);

        g.DrawEllipse(
            outline,
            p.X - 9,
            p.Y - 9,
            18,
            18);

        TextRenderer.DrawText(
            g,
            label,
            SystemFonts.DefaultFont,
            new Point(p.X + 12, p.Y - 8),
            color);
    }

    private static Point ToPixel(
        MapPoint point,
        int size) =>
        new(
            (int)Math.Round(
                point.X /
                MapPoint.MapSize *
                (size - 1)),
            (int)Math.Round(
                (MapPoint.MapSize - point.Y) /
                MapPoint.MapSize *
                (size - 1)));

    private static byte[] ToPngScaled(
        Bitmap bitmap,
        int maxDimension)
    {
        if (bitmap.Width <= maxDimension &&
            bitmap.Height <= maxDimension)
            return ToPng(bitmap);

        var scale = Math.Min(
            maxDimension / (double)bitmap.Width,
            maxDimension / (double)bitmap.Height);

        var width = Math.Max(
            1,
            (int)Math.Round(
                bitmap.Width * scale));

        var height = Math.Max(
            1,
            (int)Math.Round(
                bitmap.Height * scale));

        using var resized =
            new Bitmap(
                width,
                height,
                PixelFormat.Format24bppRgb);

        using (var g =
               Graphics.FromImage(resized))
        {
            g.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            g.DrawImage(
                bitmap,
                new Rectangle(
                    0,
                    0,
                    width,
                    height));
        }

        return ToPng(resized);
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static string NormalizeKind(string? kind) =>
        (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "blocked" => "blocked",
            "danger" => "danger",
            "clear" => "clear",
            _ => "uncertain"
        };

    private sealed class RawVisionReport
    {
        public string? Summary { get; set; }
        public List<RawVisionFinding>? Findings { get; set; }
    }

    private sealed class RawVisionFinding
    {
        public string? EdgeToken { get; set; }
        public string? Kind { get; set; }
        public double Severity { get; set; }
        public double Confidence { get; set; }
        public string? Reason { get; set; }
    }
}

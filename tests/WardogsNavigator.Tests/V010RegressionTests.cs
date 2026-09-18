using System.Drawing;
using System.Reflection;
using WardogsNavigator.Services;
using Xunit;

namespace WardogsNavigator.Tests;

public sealed class V010RegressionTests
{
    [Fact]
    public void Settings_LegacyJsonKeepsV010Defaults()
    {
        const string legacyJson = """
        {
          "GameWindowTitleContains": "WARDOGS",
          "CurrentMap": "ozeti",
          "SpeakNavigation": false,
          "PlayerRegion": {
            "X": 0.10,
            "Y": 0.20,
            "Width": 0.12,
            "Height": 0.08
          }
        }
        """;

        var settings = AppSettings.FromJson(legacyJson);

        Assert.Equal("WARDOGS", settings.GameWindowTitleContains);
        Assert.Equal("ozeti", settings.CurrentMap);
        Assert.False(settings.SpeakNavigation);
        Assert.Equal("", settings.CaptureWindowTitleContains);
        Assert.Equal(CaptureBackendMode.Auto, settings.CaptureBackend);
        Assert.Equal(RuntimePerformanceMode.Balanced, settings.PerformanceMode);
        Assert.Equal(90, settings.AiVisionCacheSeconds);
        Assert.Equal(1280, settings.AiVisionMaxImageDimension);
        Assert.True(settings.PlayerRegion.IsValid);
        Assert.NotNull(settings.TargetMarkerProfile);
        Assert.NotNull(settings.VisionMapRegion);
    }

    [Fact]
    public void Settings_InvalidNewValuesNormalizeSafely()
    {
        const string json = """
        {
          "CaptureBackend": 999,
          "PerformanceMode": 999,
          "AiVisionCacheSeconds": 0,
          "AiVisionMaxImageDimension": 0,
          "VisualTargetScanSeconds": 0
        }
        """;

        var settings = AppSettings.FromJson(json);

        Assert.Equal(CaptureBackendMode.Auto, settings.CaptureBackend);
        Assert.Equal(RuntimePerformanceMode.Balanced, settings.PerformanceMode);
        Assert.Equal(90, settings.AiVisionCacheSeconds);
        Assert.Equal(1280, settings.AiVisionMaxImageDimension);
        Assert.Equal(3, settings.VisualTargetScanSeconds);
    }

    [Fact]
    public void CaptureBackendPolicy_SelectsExpectedOrderAndFallback()
    {
        Assert.Equal(
            new[] { CaptureBackendMode.ScreenCopy, CaptureBackendMode.PrintWindow },
            CaptureBackendPolicy.GetAttemptOrder(CaptureBackendMode.Auto));

        Assert.Equal(
            new[] { CaptureBackendMode.ScreenCopy },
            CaptureBackendPolicy.GetAttemptOrder(CaptureBackendMode.ScreenCopy));

        Assert.Equal(
            new[] { CaptureBackendMode.PrintWindow },
            CaptureBackendPolicy.GetAttemptOrder(CaptureBackendMode.PrintWindow));

        Assert.True(CaptureBackendPolicy.ShouldAcceptScreenCopy(
            CaptureBackendMode.Auto,
            captureSucceeded: true,
            probablyBlank: false));

        Assert.False(CaptureBackendPolicy.ShouldAcceptScreenCopy(
            CaptureBackendMode.Auto,
            captureSucceeded: true,
            probablyBlank: true));

        Assert.True(CaptureBackendPolicy.ShouldFallbackToPrintWindow(
            CaptureBackendMode.Auto,
            screenCopySucceeded: false,
            screenCopyProbablyBlank: true));

        Assert.True(CaptureBackendPolicy.ShouldFallbackToPrintWindow(
            CaptureBackendMode.Auto,
            screenCopySucceeded: true,
            screenCopyProbablyBlank: true));

        Assert.False(CaptureBackendPolicy.ShouldFallbackToPrintWindow(
            CaptureBackendMode.ScreenCopy,
            screenCopySucceeded: false,
            screenCopyProbablyBlank: true));
    }

    [Fact]
    public void AiVisionCachePolicy_MatchesRouteHashAndTtl()
    {
        var now = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
        var routeA = AiVisionCachePolicy.BuildRouteKey(
            "bakurani",
            new[] { "edge-b", "edge-a", "edge-a" });
        var routeB = AiVisionCachePolicy.BuildRouteKey(
            "bakurani",
            new[] { "EDGE-A", "edge-b" });

        Assert.Equal(
            routeA,
            routeB,
            ignoreCase: true);

        const ulong hash = 0x0F0F0F0F0F0F0F0FUL;
        var nearHash = hash ^ 0b1011UL;

        Assert.True(AiVisionCachePolicy.IsHit(
            routeA,
            hash,
            now.AddSeconds(-30),
            routeB,
            nearHash,
            now,
            90));

        Assert.False(AiVisionCachePolicy.IsHit(
            routeA,
            hash,
            now.AddSeconds(-91),
            routeB,
            nearHash,
            now,
            90));

        Assert.False(AiVisionCachePolicy.IsHit(
            routeA,
            hash,
            now.AddSeconds(-30),
            "ozeti|edge-a,edge-b",
            nearHash,
            now,
            90));
    }

    [Fact]
    public async Task AiVisionService_CacheHitSkipsNetworkAnalysis()
    {
        using var screenshot = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(screenshot))
        {
            g.Clear(Color.DarkSlateGray);
            g.DrawLine(Pens.White, 0, 63, 63, 0);
            g.FillRectangle(Brushes.OrangeRed, 34, 18, 8, 8);
        }

        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "a", Position = new MapPoint(10, 10) },
                new() { Id = "b", Position = new MapPoint(11, 10) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "ab", A = "a", B = "b", Verified = true }
            }
        };

        var route = new RoutePlan
        {
            MapId = "test",
            Points = new List<MapPoint>
            {
                new(10, 10),
                new(11, 10)
            },
            EdgeIds = new List<string> { "ab" }
        };

        using var maps = new MapAssetService();
        var service = new AiVisionNavigationService(
            new DeepSeekClient(),
            maps);

        var routeKey = AiVisionCachePolicy.BuildRouteKey(
            "test",
            new[] { "ab" });
        var visualHash = AiVisionCachePolicy.ComputeDHash(screenshot);

        var serviceType = typeof(AiVisionNavigationService);
        var cacheType = serviceType.GetNestedType(
            "VisionCacheEntry",
            BindingFlags.NonPublic)!;
        var cache = Activator.CreateInstance(cacheType)!;

        cacheType.GetProperty("RouteKey")!.SetValue(cache, routeKey);
        cacheType.GetProperty("VisualHash")!.SetValue(cache, visualHash);
        cacheType.GetProperty("CreatedUtc")!.SetValue(cache, DateTime.UtcNow);
        cacheType.GetProperty("Report")!.SetValue(
            cache,
            new AiVisionNavigationReport
            {
                Summary = "cached-result",
                Findings = new List<AiVisionFinding>
                {
                    new()
                    {
                        EdgeId = "ab",
                        Kind = "clear",
                        Confidence = 0.95,
                        Severity = 0,
                        Reason = "cached"
                    }
                }
            });

        serviceType.GetField(
                "_cache",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, cache);

        var result = await service.AnalyzeRouteAsync(
            "network-must-not-be-used",
            "test",
            graph,
            route,
            screenshot,
            null,
            null,
            cacheSeconds: 90,
            maxImageDimension: 1280,
            imageDetail: "low");

        Assert.Equal("cached-result", result.Summary);
        Assert.Single(result.Findings);
        Assert.Equal(1, service.CacheHits);
        Assert.Equal(0, service.CacheMisses);
    }
}

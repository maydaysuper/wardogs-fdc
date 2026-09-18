using WardogsNavigator.Services;
using Xunit;

namespace WardogsNavigator.Tests;

public sealed class V011RegressionTests
{
    [Fact]
    public void Settings_NewVisualDrivingDefaultsAreSafe()
    {
        var settings =
            AppSettings.FromJson("{}");

        Assert.True(
            settings.VisualContinuousLocalizationEnabled);
        Assert.Equal(
            1800,
            settings.VisualHudSpeedScanMilliseconds);
        Assert.Equal(
            140,
            settings.VisualMapMatchMaxMeters);
        Assert.NotNull(
            settings.DrivingViewRegion);
        Assert.NotNull(
            settings.SpeedHudRegion);
        Assert.False(
            settings.AutoReadTarget);
    }

    [Fact]
    public void Settings_InvalidVisualDrivingValuesNormalize()
    {
        const string json = """
        {
          "VisualHudSpeedScanMilliseconds": 1,
          "VisualMapMatchMaxMeters": 9999
        }
        """;

        var settings =
            AppSettings.FromJson(
                json);

        Assert.Equal(
            600,
            settings.VisualHudSpeedScanMilliseconds);
        Assert.Equal(
            300,
            settings.VisualMapMatchMaxMeters);
    }

    [Fact]
    public void SpeedOcrParser_PicksPlausibleHudSpeed()
    {
        Assert.True(
            CoordinateRecognizer
                .TryParseSpeedText(
                    "87 km/h  gear 3",
                    out var speed));

        Assert.Equal(
            87,
            speed,
            3);

        Assert.False(
            CoordinateRecognizer
                .TryParseSpeedText(
                    "no digits",
                    out _));
    }

    [Fact]
    public void RoadGraphMapMatcher_ProjectsOntoHeadingCompatibleRoad()
    {
        var graph =
            new RoadGraph
            {
                MapId = "test",
                Nodes =
                    new List<RoadNode>
                    {
                        new()
                        {
                            Id = "a",
                            Position =
                                new MapPoint(
                                    10,
                                    10)
                        },
                        new()
                        {
                            Id = "b",
                            Position =
                                new MapPoint(
                                    20,
                                    10)
                        }
                    },
                Edges =
                    new List<RoadEdge>
                    {
                        new()
                        {
                            Id = "ab",
                            A = "a",
                            B = "b",
                            Verified = true,
                            Class =
                                RoadClass.Primary
                        }
                    }
            };

        var result =
            new RoadGraphMapMatcher()
                .Match(
                    graph,
                    new MapPoint(
                        15,
                        10.5),
                    90,
                    maxDistanceMeters: 100);

        Assert.True(
            result.Success);
        Assert.Equal(
            "ab",
            result.EdgeId);
        Assert.InRange(
            result.ProjectedPoint.X,
            14.99,
            15.01);
        Assert.InRange(
            result.ProjectedPoint.Y,
            9.99,
            10.01);
        Assert.InRange(
            result.DistanceMeters,
            49,
            51);
        Assert.True(
            result.Confidence > 0.5);
    }

    [Fact]
    public void VisualDrivingTracker_IntegratesHudSpeedAndSnapsToRoad()
    {
        var graph =
            new RoadGraph
            {
                MapId = "test",
                Nodes =
                    new List<RoadNode>
                    {
                        new()
                        {
                            Id = "a",
                            Position =
                                new MapPoint(
                                    10,
                                    10)
                        },
                        new()
                        {
                            Id = "b",
                            Position =
                                new MapPoint(
                                    20,
                                    10)
                        }
                    },
                Edges =
                    new List<RoadEdge>
                    {
                        new()
                        {
                            Id = "ab",
                            A = "a",
                            B = "b",
                            Verified = true
                        }
                    }
            };

        var started =
            new DateTime(
                2026,
                9,
                19,
                0,
                0,
                0,
                DateTimeKind.Utc);

        var tracker =
            new VisualDrivingTracker();

        tracker.Start(
            new MapPoint(
                10,
                10),
            90,
            started);

        var state =
            tracker.Update(
                new VisualMotionSample
                {
                    TimestampUtc =
                        started.AddSeconds(1),
                    DeltaSeconds = 1,
                    IsMoving = true,
                    Motion01 = 0.6,
                    Confidence = 0.8
                },
                hudSpeedKmh: 36,
                fallbackVehicleSpeedKmh: 80,
                nowUtc:
                    started.AddSeconds(1),
                graph,
                mapMatchMaxMeters: 100);

        Assert.InRange(
            state.CumulativeDistanceMeters,
            9.9,
            10.1);
        Assert.InRange(
            state.Position.X,
            10.09,
            10.11);
        Assert.InRange(
            state.Position.Y,
            9.999,
            10.001);
        Assert.Equal(
            "ab",
            state.EdgeId);
        Assert.Equal(
            "HUD",
            state.SpeedSource);
        Assert.True(
            state.Confidence > 0.5);
    }
    [Theory]
    [InlineData(GlobalNavigationHotKeys.CaptureCurrentId, GlobalNavigationHotKeyAction.CaptureCurrent)]
    [InlineData(GlobalNavigationHotKeys.CaptureTargetId, GlobalNavigationHotKeyAction.CaptureTarget)]
    [InlineData(GlobalNavigationHotKeys.ToggleNavigationId, GlobalNavigationHotKeyAction.ToggleNavigation)]
    public void GlobalHotKeys_MapIdsToExpectedActions(
        int id,
        GlobalNavigationHotKeyAction expected)
    {
        Assert.True(
            GlobalNavigationHotKeys.TryGetAction(
                id,
                out var action));

        Assert.Equal(
            expected,
            action);
    }

    [Fact]
    public void GlobalHotKeys_RejectUnknownId()
    {
        Assert.False(
            GlobalNavigationHotKeys.TryGetAction(
                -1,
                out _));
    }
}

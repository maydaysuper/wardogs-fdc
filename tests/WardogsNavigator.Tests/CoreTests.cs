using WardogsNavigator.Services;
using Xunit;

namespace WardogsNavigator.Tests;

public sealed class CoreTests
{
    [Fact]
    public void CoordinateMath_UsesNorthClockwiseBearing()
    {
        var a = new MapPoint(10, 10);
        Assert.Equal(90, a.BearingDegTo(new MapPoint(11, 10)), 6);
        Assert.Equal(0, a.BearingDegTo(new MapPoint(10, 11)), 6);
        Assert.Equal(100, a.DistanceMeters(new MapPoint(11, 10)), 6);
    }

    [Fact]
    public void FireControl_DirectionMilsStayNormalized()
    {
        var solution = FireControl.Calculate(
            new MapPoint(10, 10),
            new MapPoint(9, 10));

        Assert.InRange(solution.DirectionMils, 0, 6400);
        Assert.Equal(4800, solution.DirectionMils, 4);
    }

    [Fact]
    public void CoordinateParser_ReadsNamedXY()
    {
        Assert.True(
            CoordinateRecognizer.TryParseText(
                "X: 80.52  Y: 69.85",
                out var point));

        Assert.Equal(80.52, point.X, 2);
        Assert.Equal(69.85, point.Y, 2);
    }

    [Fact]
    public void RoadGraphRouter_FollowsCalibratedNetwork()
    {
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "a", Position = new MapPoint(10, 10) },
                new() { Id = "b", Position = new MapPoint(10, 20) },
                new() { Id = "c", Position = new MapPoint(20, 20) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "ab", A = "a", B = "b", Class = RoadClass.Primary, Verified = true },
                new() { Id = "bc", A = "b", B = "c", Class = RoadClass.Primary, Verified = true }
            }
        };

        var route = new RoadGraphRouter().TryPlan(
            graph,
            new MapPoint(10.1, 10.1),
            new MapPoint(19.9, 20.1),
            RoutePreference.Shortest);

        Assert.NotNull(route);
        Assert.Contains(route!.Points, p => p.DistanceMeters(new MapPoint(10, 20)) < 5);
        Assert.True(route.DistanceKm > 1.7);
    }

    [Fact]
    public void TraceLearning_FiltersJitterAndTeleport()
    {
        var learner = new TraceLearningService();
        learner.Start(new MapPoint(10, 10));

        Assert.False(learner.Accept(new MapPoint(10.01, 10.01)));
        Assert.True(learner.Accept(new MapPoint(10.20, 10.00)));
        Assert.False(learner.Accept(new MapPoint(20, 20)));
        Assert.True(learner.Accept(new MapPoint(10.40, 10.05)));

        var result = learner.StopAndSimplify();
        Assert.True(result.Count >= 2);
        Assert.DoesNotContain(result, p => p.X > 15);
    }

    [Fact]
    public void AutoRoadMerge_PreservesManualAndTraceData()
    {
        var temp = Path.Combine(
            Path.GetTempPath(),
            "WardogsNavigatorTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            var store = new RoadGraphStore(temp);
            var graph = new RoadGraph
            {
                MapId = "test",
                Nodes = new List<RoadNode>
                {
                    new() { Id = "m1", Position = new MapPoint(10, 10) },
                    new() { Id = "m2", Position = new MapPoint(11, 10) }
                },
                Edges = new List<RoadEdge>
                {
                    new()
                    {
                        Id = "manual-edge",
                        A = "m1",
                        B = "m2",
                        Source = "manual",
                        Verified = true
                    }
                }
            };

            var auto = new RoadGraph
            {
                MapId = "test",
                Nodes = new List<RoadNode>
                {
                    new() { Id = "a1", Position = new MapPoint(20, 20) },
                    new() { Id = "a2", Position = new MapPoint(21, 20) }
                },
                Edges = new List<RoadEdge>
                {
                    new()
                    {
                        Id = "auto-edge",
                        A = "a1",
                        B = "a2",
                        Source = "auto",
                        Verified = false,
                        AutoScore = 0.75
                    }
                }
            };

            store.ReplaceAutoGraph(graph, auto);

            Assert.Contains(graph.Edges, e => e.Id == "manual-edge");
            Assert.Contains(graph.Edges, e => e.Source == "auto");

            store.ReplaceAutoGraph(
                graph,
                new RoadGraph { MapId = "test" });

            Assert.Contains(graph.Edges, e => e.Id == "manual-edge");
            Assert.DoesNotContain(graph.Edges, e => e.Source == "auto");
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void DrivenTrace_UpgradesAutoEdge()
    {
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
                new()
                {
                    Id = "auto",
                    A = "a",
                    B = "b",
                    Source = "auto",
                    Verified = false,
                    AutoScore = 0.78
                }
            }
        };

        RoadGraphStore.AddOrTouchEdge(
            graph,
            "a",
            "b",
            RoadClass.Secondary,
            "trace",
            true,
            incrementTraversal: true);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal("trace", edge.Source);
        Assert.True(edge.Verified);
        Assert.Equal(1, edge.Traversals);
        Assert.Equal(0, edge.AutoScore);
    }

    [Fact]
    public void EconomyOptimizer_RemainsDeterministicAndIndependent()
    {
        var engine = new EconomyEngine(new[]
        {
            new VehicleSpec
            {
                Id = "ural",
                NameZh = "乌拉尔",
                Price = 5000,
                SpeedKmh = 79,
                Passengers = 3,
                PalletSlots = 2,
                FuelL = 85,
                RangeKm = 21
            },
            new VehicleSpec
            {
                Id = "taxi",
                NameZh = "Taxi",
                Price = 1000,
                SpeedKmh = 120,
                Passengers = 4,
                PalletSlots = 0,
                FuelL = 40,
                RangeKm = 20
            }
        });

        var destination = new Destination
        {
            Id = "fob",
            Label = "FOB",
            Kind = DestinationKind.Fob,
            Position = new MapPoint(20, 10)
        };

        var plans = engine.Optimize(
            new MapPoint(10, 10),
            new[] { destination },
            3);

        Assert.NotEmpty(plans);
        Assert.Contains(
            plans,
            p => p.Vehicle.Id == "ural" && p.Pallets == 2);
    }
}

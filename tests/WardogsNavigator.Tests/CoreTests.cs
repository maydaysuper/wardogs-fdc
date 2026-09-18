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
    public void VehicleProfiles_CanChooseDifferentFastestRoads()
    {
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "start", Position = new MapPoint(9, 10) },
                new() { Id = "s", Position = new MapPoint(10, 10) },
                new() { Id = "p1", Position = new MapPoint(10, 14) },
                new() { Id = "p2", Position = new MapPoint(14, 14) },
                new() { Id = "t1", Position = new MapPoint(12, 11) },
                new() { Id = "e", Position = new MapPoint(14, 10) },
                new() { Id = "end", Position = new MapPoint(15, 10) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "common-start", A = "start", B = "s", Class = RoadClass.Primary, Verified = true },
                new() { Id = "sp1", A = "s", B = "p1", Class = RoadClass.Primary, Verified = true },
                new() { Id = "p1p2", A = "p1", B = "p2", Class = RoadClass.Primary, Verified = true },
                new() { Id = "p2e", A = "p2", B = "e", Class = RoadClass.Primary, Verified = true },
                new() { Id = "st1", A = "s", B = "t1", Class = RoadClass.Track, Verified = true },
                new() { Id = "t1e", A = "t1", B = "e", Class = RoadClass.Track, Verified = true },
                new() { Id = "common-end", A = "e", B = "end", Class = RoadClass.Primary, Verified = true }
            }
        };

        var truck = new VehicleRoutingProfile
        {
            VehicleId = "ural",
            PrimaryFactor = 1.0,
            SecondaryFactor = 0.75,
            TrackFactor = 0.35,
            BridgeFactor = 0.8,
            RiskTolerance = 0.4
        };

        var buggy = new VehicleRoutingProfile
        {
            VehicleId = "buggy",
            PrimaryFactor = 1.0,
            SecondaryFactor = 0.9,
            TrackFactor = 1.0,
            BridgeFactor = 0.9,
            RiskTolerance = 0.7
        };

        var router = new RoadGraphRouter();
        var truckRoute = router.TryPlan(
            graph,
            new MapPoint(9.1, 10),
            new MapPoint(14.9, 10),
            RoutePreference.Fastest,
            truck);

        var buggyRoute = router.TryPlan(
            graph,
            new MapPoint(9.1, 10),
            new MapPoint(14.9, 10),
            RoutePreference.Fastest,
            buggy);

        Assert.NotNull(truckRoute);
        Assert.NotNull(buggyRoute);
        Assert.Contains("p1p2", truckRoute!.EdgeIds);
        Assert.DoesNotContain("t1e", truckRoute.EdgeIds);
        Assert.Contains("t1e", buggyRoute!.EdgeIds);
        Assert.DoesNotContain("p1p2", buggyRoute.EdgeIds);
    }

    [Fact]
    public void SafeRoute_AvoidsTemporaryHazard()
    {
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "start", Position = new MapPoint(9, 10) },
                new() { Id = "s", Position = new MapPoint(10, 10) },
                new() { Id = "north", Position = new MapPoint(12, 12) },
                new() { Id = "south", Position = new MapPoint(12, 8) },
                new() { Id = "e", Position = new MapPoint(14, 10) },
                new() { Id = "end", Position = new MapPoint(15, 10) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "common-start", A = "start", B = "s", Class = RoadClass.Primary, Verified = true },
                new() { Id = "sn", A = "s", B = "north", Class = RoadClass.Primary, Verified = true },
                new() { Id = "ne", A = "north", B = "e", Class = RoadClass.Primary, Verified = true },
                new() { Id = "ss", A = "s", B = "south", Class = RoadClass.Primary, Verified = true },
                new() { Id = "se", A = "south", B = "e", Class = RoadClass.Primary, Verified = true },
                new() { Id = "common-end", A = "e", B = "end", Class = RoadClass.Primary, Verified = true }
            }
        };

        var hazards = new List<NavigationHazard>
        {
            new()
            {
                MapId = "test",
                Center = new MapPoint(11, 11),
                RadiusMeters = 180,
                Severity = 1,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(10)
            }
        };

        var route = new RoadGraphRouter().TryPlan(
            graph,
            new MapPoint(9.1, 10),
            new MapPoint(14.9, 10),
            RoutePreference.Safe,
            VehicleRoutingProfileService.Generic(),
            hazards);

        Assert.NotNull(route);
        Assert.Contains("ss", route!.EdgeIds);
        Assert.Contains("se", route.EdgeIds);
        Assert.DoesNotContain("sn", route.EdgeIds);
        Assert.DoesNotContain("ne", route.EdgeIds);
    }

    [Fact]
    public void AiLearning_AppliesOnlyHighConfidenceSuggestions()
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
                new() { Id = "ab", A = "a", B = "b", Verified = true }
            }
        };

        var temp = Path.Combine(
            Path.GetTempPath(),
            "WardogsNavigatorTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            var store = new RoadGraphStore(temp);
            var applied = store.ApplyAiSuggestions(
                graph,
                new[]
                {
                    new AiRoadSuggestion
                    {
                        EdgeId = "ab",
                        VehicleId = "ural",
                        RiskDelta = 0.10,
                        SpeedMultiplier = 0.72,
                        Confidence = 0.90,
                        Reason = "multiple slow traversals"
                    },
                    new AiRoadSuggestion
                    {
                        EdgeId = "ab",
                        VehicleId = "buggy",
                        RiskDelta = 0.20,
                        SpeedMultiplier = 0.60,
                        Confidence = 0.30,
                        Reason = "insufficient sample"
                    }
                },
                0.72);

            Assert.Equal(1, applied);
            var edge = Assert.Single(graph.Edges);
            Assert.Equal(0, edge.Risk, 6);
            Assert.Equal(0.10, edge.AiRiskAdjustment, 6);
            Assert.Equal(0.72, edge.VehicleSpeedMultipliers["ural"], 6);
            Assert.False(edge.VehicleSpeedMultipliers.ContainsKey("buggy"));
            Assert.Equal(0.90, edge.AiConfidence, 6);

            Assert.Equal(1, store.RemoveAiLearning(graph));
            Assert.Equal(0, edge.AiRiskAdjustment, 6);
            Assert.Empty(edge.VehicleSpeedMultipliers);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void NavigationLearningSession_StoresRouteExperience()
    {
        var route = new RoutePlan
        {
            MapId = "test",
            Preference = RoutePreference.Fastest,
            DistanceKm = 1.2,
            EdgeIds = new List<string> { "a", "b" },
            Points = new List<MapPoint>
            {
                new(10, 10),
                new(11, 10)
            }
        };

        var session = new NavigationLearningSession();
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "n1", Position = new MapPoint(10, 10) },
                new() { Id = "n2", Position = new MapPoint(11, 10) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "a", A = "n1", B = "n2", Verified = true }
            }
        };

        session.Start(
            "test",
            "ural",
            RoutePreference.Fastest,
            route,
            graph,
            new MapPoint(10, 10));

        session.NotePoint(new MapPoint(10.5, 10));
        session.NotePoint(new MapPoint(11, 10));

        var result = session.Stop(completed: true);

        Assert.NotNull(result);
        Assert.True(result!.Completed);
        Assert.Equal("ural", result.VehicleId);
        Assert.Contains("a", result.EdgeIds);
        Assert.True(result.ActualDistanceKm > 0.09);
        Assert.Contains(result.EdgeObservations, x => x.EdgeId == "a");
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

using System.Drawing;
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
    public void NormalNavigation_CanPromoteStrongAutoRoadEvidence()
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
                    Id = "ab",
                    A = "a",
                    B = "b",
                    Source = "auto",
                    Verified = false,
                    AutoScore = 0.75
                }
            }
        };

        var experience = new NavigationExperience
        {
            MapId = "test",
            VehicleId = "ural",
            Completed = true,
            EdgeObservations = new List<EdgeTravelObservation>
            {
                new()
                {
                    EdgeId = "ab",
                    Samples = 5,
                    DistanceKm = 0.08,
                    Seconds = 8,
                    MaxDeviationMeters = 20
                }
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
            Assert.Equal(1, store.RecordNavigationExperience(graph, experience));

            var edge = Assert.Single(graph.Edges);
            Assert.Equal("trace", edge.Source);
            Assert.True(edge.Verified);
            Assert.Equal(1, edge.Traversals);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
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
    public void VisionEvidence_AppliesOnlyActionableHighConfidenceFindings()
    {
        var temp = Path.Combine(
            Path.GetTempPath(),
            "WardogsNavigatorTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            var store = new NavigationVisionEvidenceStore(temp);

            var report = new AiVisionNavigationReport
            {
                Summary = "test",
                Findings = new List<AiVisionFinding>
                {
                    new()
                    {
                        EdgeId = "e1",
                        Kind = "blocked",
                        Severity = 0.90,
                        Confidence = 0.90,
                        Reason = "clear obstruction"
                    },
                    new()
                    {
                        EdgeId = "e2",
                        Kind = "uncertain",
                        Severity = 1.00,
                        Confidence = 0.99,
                        Reason = "cannot align"
                    },
                    new()
                    {
                        EdgeId = "e3",
                        Kind = "danger",
                        Severity = 0.80,
                        Confidence = 0.60,
                        Reason = "weak evidence"
                    },
                    new()
                    {
                        EdgeId = "not-real",
                        Kind = "blocked",
                        Severity = 1.00,
                        Confidence = 1.00,
                        Reason = "invalid id"
                    }
                }
            };

            var valid = new HashSet<string>(
                new[] { "e1", "e2", "e3" },
                StringComparer.OrdinalIgnoreCase);

            var applied = store.ApplyReport(
                "test",
                report,
                valid,
                0.80,
                TimeSpan.FromMinutes(5));

            Assert.Equal(1, applied);

            var active = store.GetActive("test");
            var evidence = Assert.Single(active);
            Assert.Equal("e1", evidence.EdgeId);
            Assert.Equal("blocked", evidence.Kind);

            var risk = store.GetRiskMap("test");
            Assert.True(risk["e1"] > 0.80);

            Assert.Equal(
                1,
                store.ClearEdges(
                    "test",
                    new[] { "e1" }));

            Assert.Empty(store.GetActive("test"));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void SafeRoute_AvoidsHighConfidenceVisualRisk()
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

        var vision = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["sn"] = 0.95,
            ["ne"] = 0.95
        };

        var route = new RoadGraphRouter().TryPlan(
            graph,
            new MapPoint(9.1, 10),
            new MapPoint(14.9, 10),
            RoutePreference.Safe,
            VehicleRoutingProfileService.Generic(),
            Array.Empty<NavigationHazard>(),
            vision);

        Assert.NotNull(route);
        Assert.Contains("ss", route!.EdgeIds);
        Assert.Contains("se", route.EdgeIds);
        Assert.DoesNotContain("sn", route.EdgeIds);
        Assert.DoesNotContain("ne", route.EdgeIds);
    }

    [Fact]
    public void Guidance_BuildsRightTurnManeuver()
    {
        var points = new List<MapPoint>
        {
            new(10, 10),
            new(10, 12),
            new(12, 12),
            new(14, 12)
        };

        var maneuvers =
            NavigationGuidance.BuildManeuvers(points);

        var turn = Assert.Single(
            maneuvers.Where(x =>
                x.Kind ==
                NavigationManeuverKind.TurnRight));

        Assert.Equal(1, turn.RoutePointIndex);
        Assert.InRange(turn.TurnDegrees, 89, 91);
    }

    [Fact]
    public void Guidance_SingleOcrJumpDoesNotImmediatelyReroute()
    {
        var route = new RoutePlan
        {
            MapId = "test",
            DistanceKm = 1.0,
            EstimatedMinutes = 1.0,
            Points = new List<MapPoint>
            {
                new(10, 10),
                new(20, 10)
            }
        };

        var tracker =
            new NavigationGuidanceTracker();
        tracker.Reset(route);

        var first =
            tracker.BuildCue(
                new MapPoint(12, 10.80));

        Assert.True(first.OffRoute);
        Assert.False(first.ShouldReroute);

        var recovered =
            tracker.BuildCue(
                new MapPoint(12.5, 10.05));

        Assert.False(recovered.ShouldReroute);
        Assert.False(recovered.OffRoute);
    }

    [Fact]
    public void Guidance_PersistentDeviationTriggersReroute()
    {
        var route = new RoutePlan
        {
            MapId = "test",
            DistanceKm = 1.0,
            EstimatedMinutes = 1.0,
            Points = new List<MapPoint>
            {
                new(10, 10),
                new(20, 10)
            }
        };

        var tracker =
            new NavigationGuidanceTracker();
        tracker.Reset(route);

        var first =
            tracker.BuildCue(
                new MapPoint(12, 10.80));

        var second =
            tracker.BuildCue(
                new MapPoint(12.5, 10.82));

        Assert.False(first.ShouldReroute);
        Assert.True(second.ShouldReroute);
        Assert.True(second.DeviationMeters >= 75);
    }

    [Fact]
    public void Guidance_TracksProgressAndDynamicEta()
    {
        var route = new RoutePlan
        {
            MapId = "test",
            DistanceKm = 1.0,
            EstimatedMinutes = 2.0,
            Points = new List<MapPoint>
            {
                new(10, 10),
                new(20, 10)
            }
        };

        var tracker =
            new NavigationGuidanceTracker();
        tracker.Reset(route);

        var cue =
            tracker.BuildCue(
                new MapPoint(15, 10));

        Assert.InRange(cue.Progress01, 0.49, 0.51);
        Assert.InRange(cue.RemainingKm, 0.49, 0.51);
        Assert.InRange(cue.RemainingMinutes, 0.99, 1.01);
    }

    [Fact]
    public void RealDriving_LearnsVehicleSpeedWithoutAiLayer()
    {
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new()
                {
                    Id = "a",
                    Position = new MapPoint(10, 10)
                },
                new()
                {
                    Id = "b",
                    Position = new MapPoint(11, 10)
                }
            },
            Edges = new List<RoadEdge>
            {
                new()
                {
                    Id = "ab",
                    A = "a",
                    B = "b",
                    Class = RoadClass.Primary,
                    Verified = true
                }
            }
        };

        var experience =
            new NavigationExperience
            {
                MapId = "test",
                VehicleId = "ural",
                VehicleBaseSpeedKmh = 79,
                Completed = true,
                EdgeObservations =
                    new List<EdgeTravelObservation>
                    {
                        new()
                        {
                            EdgeId = "ab",
                            Samples = 6,
                            DistanceKm = 0.10,
                            Seconds = 8,
                            MaxDeviationMeters = 12
                        }
                    }
            };

        var temp = Path.Combine(
            Path.GetTempPath(),
            "WardogsNavigatorTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var store =
                new RoadGraphStore(temp);

            Assert.Equal(
                1,
                store.RecordNavigationExperience(
                    graph,
                    experience));

            var edge = Assert.Single(
                graph.Edges);

            Assert.True(
                edge.LocalVehicleSpeedMultipliers
                    .ContainsKey("ural"));

            Assert.InRange(
                edge.LocalVehicleSpeedMultipliers["ural"],
                0.55,
                0.70);

            store.ApplyAiSuggestions(
                graph,
                new[]
                {
                    new AiRoadSuggestion
                    {
                        EdgeId = "ab",
                        VehicleId = "ural",
                        SpeedMultiplier = 0.90,
                        Confidence = 0.95
                    }
                });

            Assert.Equal(
                1,
                store.RemoveAiLearning(graph));

            Assert.True(
                edge.LocalVehicleSpeedMultipliers
                    .ContainsKey("ural"));

            Assert.False(
                edge.VehicleSpeedMultipliers
                    .ContainsKey("ural"));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void LearnedSlowRoadCanChangeFastestRoute()
    {
        var learned = new LocalSpeedLearningState
        {
            ObservationCount = 6,
            EffectiveWeight = 12,
            MeanMultiplier = 0.55,
            Confidence = 0.95
        };

        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "start", Position = new MapPoint(9, 10) },
                new() { Id = "s", Position = new MapPoint(10, 10) },
                new() { Id = "short", Position = new MapPoint(12, 10) },
                new() { Id = "long", Position = new MapPoint(12, 11.5) },
                new() { Id = "e", Position = new MapPoint(14, 10) },
                new() { Id = "end", Position = new MapPoint(15, 10) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "common-start", A = "start", B = "s", Class = RoadClass.Primary, Verified = true },
                new()
                {
                    Id = "short-a",
                    A = "s",
                    B = "short",
                    Class = RoadClass.Primary,
                    Verified = true,
                    LocalVehicleSpeedMultipliers =
                        new Dictionary<string, double>
                        {
                            ["ural"] = 0.55
                        },
                    LocalVehicleSpeedLearning =
                        new Dictionary<string, LocalSpeedLearningState>
                        {
                            ["ural"] = new()
                            {
                                ObservationCount = learned.ObservationCount,
                                EffectiveWeight = learned.EffectiveWeight,
                                MeanMultiplier = learned.MeanMultiplier,
                                Confidence = learned.Confidence
                            }
                        }
                },
                new()
                {
                    Id = "short-b",
                    A = "short",
                    B = "e",
                    Class = RoadClass.Primary,
                    Verified = true,
                    LocalVehicleSpeedMultipliers =
                        new Dictionary<string, double>
                        {
                            ["ural"] = 0.55
                        },
                    LocalVehicleSpeedLearning =
                        new Dictionary<string, LocalSpeedLearningState>
                        {
                            ["ural"] = new()
                            {
                                ObservationCount = learned.ObservationCount,
                                EffectiveWeight = learned.EffectiveWeight,
                                MeanMultiplier = learned.MeanMultiplier,
                                Confidence = learned.Confidence
                            }
                        }
                },
                new() { Id = "long-a", A = "s", B = "long", Class = RoadClass.Primary, Verified = true },
                new() { Id = "long-b", A = "long", B = "e", Class = RoadClass.Primary, Verified = true },
                new() { Id = "common-end", A = "e", B = "end", Class = RoadClass.Primary, Verified = true }
            }
        };

        var profile =
            VehicleRoutingProfileService
                .ForVehicleId("ural");

        var route =
            new RoadGraphRouter().TryPlan(
                graph,
                new MapPoint(9.1, 10),
                new MapPoint(14.9, 10),
                RoutePreference.Fastest,
                profile);

        Assert.NotNull(route);
        Assert.Contains(
            "long-a",
            route!.EdgeIds);
        Assert.Contains(
            "long-b",
            route.EdgeIds);
        Assert.DoesNotContain(
            "short-a",
            route.EdgeIds);
    }

    [Fact]
    public void PositionFilter_RejectsSingleLargeOcrJump()
    {
        var filter =
            new NavigationPositionFilter();

        var t0 =
            new DateTime(
                2026,
                9,
                18,
                0,
                0,
                0,
                DateTimeKind.Utc);

        Assert.True(
            filter.TryAccept(
                new MapPoint(10, 10),
                t0,
                out var first));

        Assert.Equal(
            10,
            first.X,
            6);

        Assert.False(
            filter.TryAccept(
                new MapPoint(50, 50),
                t0.AddSeconds(1),
                out _));

        Assert.True(
            filter.TryAccept(
                new MapPoint(10.20, 10.01),
                t0.AddSeconds(2),
                out var recovered));

        Assert.InRange(
            recovered.X,
            10.0,
            10.2);

        Assert.Equal(
            1,
            filter.RejectedSamples);
    }

    [Fact]
    public void PositionFilter_ReanchorsAfterThreeConsistentLargeMoves()
    {
        var filter =
            new NavigationPositionFilter();

        var t0 =
            new DateTime(
                2026,
                9,
                18,
                0,
                0,
                0,
                DateTimeKind.Utc);

        Assert.True(
            filter.TryAccept(
                new MapPoint(10, 10),
                t0,
                out _));

        Assert.False(
            filter.TryAccept(
                new MapPoint(30, 30),
                t0.AddSeconds(1),
                out _));

        Assert.False(
            filter.TryAccept(
                new MapPoint(30.02, 30.01),
                t0.AddSeconds(2),
                out _));

        Assert.True(
            filter.TryAccept(
                new MapPoint(30.01, 30.02),
                t0.AddSeconds(3),
                out var reanchored));

        Assert.InRange(
            reanchored.X,
            30.0,
            30.1);
    }

    [Fact]
    public void LocalSpeedLearning_GainsConfidenceAcrossTrips()
    {
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new()
                {
                    Id = "a",
                    Position = new MapPoint(10, 10)
                },
                new()
                {
                    Id = "b",
                    Position = new MapPoint(11, 10)
                }
            },
            Edges = new List<RoadEdge>
            {
                new()
                {
                    Id = "ab",
                    A = "a",
                    B = "b",
                    Class = RoadClass.Primary,
                    Verified = true
                }
            }
        };

        var experience = new NavigationExperience
        {
            MapId = "test",
            VehicleId = "ural",
            VehicleBaseSpeedKmh = 79,
            Completed = true,
            EdgeObservations =
                new List<EdgeTravelObservation>
                {
                    new()
                    {
                        EdgeId = "ab",
                        Samples = 8,
                        DistanceKm = 0.10,
                        Seconds = 8,
                        MaxDeviationMeters = 8
                    }
                }
        };

        var temp = Path.Combine(
            Path.GetTempPath(),
            "WardogsNavigatorTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            var store =
                new RoadGraphStore(temp);

            store.RecordNavigationExperience(
                graph,
                experience);

            var state1 =
                graph.Edges[0]
                    .LocalVehicleSpeedLearning["ural"];

            var confidence1 =
                state1.Confidence;

            Assert.InRange(
                confidence1,
                0.10,
                0.70);

            store.RecordNavigationExperience(
                graph,
                experience);

            var state2 =
                graph.Edges[0]
                    .LocalVehicleSpeedLearning["ural"];

            Assert.True(
                state2.Confidence >
                confidence1);

            Assert.Equal(
                2,
                state2.ObservationCount);

            Assert.InRange(
                state2.MeanMultiplier,
                0.55,
                0.75);
        }
        finally
        {
            Directory.Delete(
                temp,
                true);
        }
    }

    [Fact]
    public void FastestRoute_PrefersSmootherPathWhileShortestKeepsDistance()
    {
        var graph = new RoadGraph
        {
            MapId = "test",
            Nodes = new List<RoadNode>
            {
                new() { Id = "start", Position = new MapPoint(9, 10) },
                new() { Id = "s", Position = new MapPoint(10, 10) },
                new() { Id = "smooth", Position = new MapPoint(12, 11.5) },
                new() { Id = "zig1", Position = new MapPoint(11.5, 9.4) },
                new() { Id = "zig2", Position = new MapPoint(12.5, 10.6) },
                new() { Id = "e", Position = new MapPoint(14, 10) },
                new() { Id = "end", Position = new MapPoint(15, 10) }
            },
            Edges = new List<RoadEdge>
            {
                new() { Id = "common-start", A = "start", B = "s", Class = RoadClass.Primary, Verified = true },
                new() { Id = "smooth-a", A = "s", B = "smooth", Class = RoadClass.Primary, Verified = true },
                new() { Id = "smooth-b", A = "smooth", B = "e", Class = RoadClass.Primary, Verified = true },
                new() { Id = "zig-a", A = "s", B = "zig1", Class = RoadClass.Primary, Verified = true },
                new() { Id = "zig-b", A = "zig1", B = "zig2", Class = RoadClass.Primary, Verified = true },
                new() { Id = "zig-c", A = "zig2", B = "e", Class = RoadClass.Primary, Verified = true },
                new() { Id = "common-end", A = "e", B = "end", Class = RoadClass.Primary, Verified = true }
            }
        };

        var router =
            new RoadGraphRouter();

        var shortest =
            router.TryPlan(
                graph,
                new MapPoint(9.1, 10),
                new MapPoint(14.9, 10),
                RoutePreference.Shortest,
                VehicleRoutingProfileService.Generic());

        var fastest =
            router.TryPlan(
                graph,
                new MapPoint(9.1, 10),
                new MapPoint(14.9, 10),
                RoutePreference.Fastest,
                VehicleRoutingProfileService.Generic());

        Assert.NotNull(shortest);
        Assert.NotNull(fastest);

        Assert.Contains(
            "zig-b",
            shortest!.EdgeIds);

        Assert.Contains(
            "smooth-a",
            fastest!.EdgeIds);

        Assert.DoesNotContain(
            "zig-b",
            fastest.EdgeIds);
    }

    [Fact]
    public void MapViewportRegistration_ConvertsScreenPixelToWorld()
    {
        var registration =
            new MapViewportRegistration
            {
                MapId = "test",
                Left01 = 0.25,
                Top01 = 0.20,
                Width01 = 0.50,
                Height01 = 0.40,
                Confidence = 0.90
            };

        var point =
            registration.ScreenPixelToWorld(
                50,
                50,
                101,
                101);

        Assert.InRange(
            point.X,
            81.8,
            82.1);

        Assert.InRange(
            point.Y,
            97.8,
            98.5);

        var pixel =
            registration.WorldToScreenPixel(
                point,
                101,
                101);

        Assert.InRange(
            pixel.X,
            49.5f,
            50.5f);

        Assert.InRange(
            pixel.Y,
            49.5f,
            50.5f);
    }

    [Fact]
    public void TargetMarkerDetector_CalibratesAndFindsColoredMarker()
    {
        using var image =
            new Bitmap(
                420,
                420);

        using (var g =
               Graphics.FromImage(image))
        {
            g.Clear(
                Color.FromArgb(
                    48,
                    55,
                    52));

            using var road =
                new Pen(
                    Color.LightGray,
                    4);

            g.DrawLine(
                road,
                20,
                350,
                390,
                80);

            using var distractor =
                new SolidBrush(
                    Color.Gold);

            g.FillEllipse(
                distractor,
                45,
                45,
                18,
                18);

            using var target =
                new SolidBrush(
                    Color.FromArgb(
                        240,
                        40,
                        70));

            g.FillEllipse(
                target,
                290,
                105,
                22,
                22);
        }

        var detector =
            new TargetMarkerDetector();

        var profile =
            detector.CreateProfile(
                image,
                new Point(
                    301,
                    116));

        Assert.True(
            profile.IsValid);

        var registration =
            new MapViewportRegistration
            {
                MapId = "test",
                Left01 = 0,
                Top01 = 0,
                Width01 = 1,
                Height01 = 1,
                Confidence = 0.95
            };

        var detection =
            detector.Detect(
                image,
                registration,
                profile);

        Assert.True(
            detection.Success);

        Assert.True(
            detection.Confidence > 0.50);

        var expected =
            registration.ScreenPixelToWorld(
                301,
                116,
                image.Width,
                image.Height);

        Assert.True(
            detection.Point.DistanceMeters(
                expected) <
            80);
    }

    [Fact]
    public void NavigationMapMemory_PersistsRegistrationAndTarget()
    {
        var temp = Path.Combine(
            Path.GetTempPath(),
            "WardogsNavigatorTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            var store =
                new NavigationMapMemoryStore(
                    temp);

            var registration =
                new MapViewportRegistration
                {
                    MapId = "bakurani",
                    Left01 = 0.20,
                    Top01 = 0.15,
                    Width01 = 0.55,
                    Height01 = 0.55,
                    Confidence = 0.84
                };

            store.RecordRegistration(
                "bakurani",
                registration);

            store.RecordTarget(
                "bakurani",
                new MapPoint(
                    70,
                    88),
                0.91);

            var reloaded =
                new NavigationMapMemoryStore(
                    temp);

            var memory =
                reloaded.Get(
                    "BAKURANI");

            Assert.NotNull(
                memory.LastRegistration);

            Assert.Equal(
                1,
                memory.SuccessfulRegistrations);

            Assert.InRange(
                memory.LastRegistration!.Left01,
                0.19,
                0.21);

            Assert.True(
                memory.LastVisualTarget.HasValue);

            Assert.True(
                memory.LastVisualTarget!.Value
                    .DistanceMeters(
                        new MapPoint(
                            70,
                            88)) <
                1);

            Assert.InRange(
                memory.LastTargetConfidence,
                0.90,
                0.92);
        }
        finally
        {
            Directory.Delete(
                temp,
                true);
        }
    }

    [Fact]
    public void VisualRegistration_FindsSyntheticMapCrop()
    {
        using var baseMap =
            new Bitmap(
                512,
                512);

        using (var g =
               Graphics.FromImage(baseMap))
        {
            g.Clear(
                Color.FromArgb(
                    35,
                    42,
                    38));

            using var road1 =
                new Pen(
                    Color.FromArgb(
                        210,
                        205,
                        190),
                    7);

            using var road2 =
                new Pen(
                    Color.FromArgb(
                        100,
                        160,
                        120),
                    5);

            g.DrawLine(
                road1,
                20,
                430,
                480,
                70);

            g.DrawLine(
                road1,
                80,
                60,
                430,
                470);

            g.DrawEllipse(
                road2,
                130,
                110,
                220,
                170);

            g.DrawRectangle(
                road2,
                285,
                55,
                120,
                95);

            using var dot =
                new SolidBrush(
                    Color.White);

            g.FillEllipse(
                dot,
                330,
                250,
                25,
                25);

            g.FillRectangle(
                dot,
                175,
                300,
                34,
                18);
        }

        var cropRect =
            new Rectangle(
                128,
                96,
                256,
                256);

        using var screenshot =
            new Bitmap(
                420,
                420);

        using (var g =
               Graphics.FromImage(screenshot))
        {
            g.DrawImage(
                baseMap,
                new Rectangle(
                    0,
                    0,
                    screenshot.Width,
                    screenshot.Height),
                cropRect,
                GraphicsUnit.Pixel);
        }

        using var assets =
            new MapAssetService();

        var service =
            new MapVisualRegistrationService(
                assets);

        var registration =
            service.RegisterLocal(
                "test",
                baseMap,
                screenshot);

        Assert.NotNull(
            registration);

        Assert.True(
            registration!.Confidence >
            0.35);

        Assert.InRange(
            registration.Left01,
            0.16,
            0.34);

        Assert.InRange(
            registration.Top01,
            0.10,
            0.30);

        Assert.InRange(
            registration.Width01,
            0.40,
            0.62);

        Assert.InRange(
            registration.Height01,
            0.40,
            0.62);
    }

    [Fact]
    public void RotatedViewport_RoundTripsWorldAndScreen()
    {
        var registration =
            new MapViewportRegistration
            {
                MapId = "test",
                Left01 = 0.28,
                Top01 = 0.24,
                Width01 = 0.42,
                Height01 = 0.36,
                RotationDeg = 37,
                Confidence = 0.90
            };

        var point =
            registration.ScreenPixelToWorld(
                147,
                82,
                301,
                201);

        var pixel =
            registration.WorldToScreenPixel(
                point,
                301,
                201);

        Assert.InRange(
            pixel.X,
            146.4f,
            147.6f);

        Assert.InRange(
            pixel.Y,
            81.4f,
            82.6f);

        Assert.True(
            registration.RotatedViewportInsideMap());
    }

    [Fact]
    public void VisualRegistration_FindsSyntheticRotatedMapCrop()
    {
        using var baseMap =
            new Bitmap(
                512,
                512);

        using (var g =
               Graphics.FromImage(baseMap))
        {
            g.Clear(
                Color.FromArgb(
                    34,
                    41,
                    37));

            using var road =
                new Pen(
                    Color.FromArgb(
                        225,
                        214,
                        190),
                    8);

            using var detail =
                new Pen(
                    Color.FromArgb(
                        82,
                        160,
                        118),
                    5);

            g.DrawLine(
                road,
                35,
                450,
                470,
                80);

            g.DrawLine(
                road,
                70,
                75,
                440,
                400);

            g.DrawArc(
                detail,
                90,
                130,
                260,
                190,
                15,
                250);

            g.DrawRectangle(
                detail,
                305,
                65,
                115,
                82);

            using var white =
                new SolidBrush(
                    Color.White);

            g.FillEllipse(
                white,
                335,
                258,
                27,
                27);

            g.FillRectangle(
                white,
                168,
                316,
                42,
                19);
        }

        var expected =
            new MapViewportRegistration
            {
                MapId = "test",
                Left01 = 0.27,
                Top01 = 0.27,
                Width01 = 0.46,
                Height01 = 0.46,
                RotationDeg = 31,
                Confidence = 1
            };

        using var screenshot =
            new Bitmap(
                320,
                320);

        for (var y = 0;
             y < screenshot.Height;
             y++)
        {
            for (var x = 0;
                 x < screenshot.Width;
                 x++)
            {
                var world =
                    expected.ScreenPixelToWorld(
                        x,
                        y,
                        screenshot.Width,
                        screenshot.Height);

                var bx =
                    Math.Clamp(
                        (int)Math.Round(
                            world.X /
                            MapPoint.MapSize *
                            (baseMap.Width - 1)),
                        0,
                        baseMap.Width - 1);

                var by =
                    Math.Clamp(
                        (int)Math.Round(
                            (
                                1 -
                                world.Y /
                                MapPoint.MapSize
                            ) *
                            (baseMap.Height - 1)),
                        0,
                        baseMap.Height - 1);

                screenshot.SetPixel(
                    x,
                    y,
                    baseMap.GetPixel(
                        bx,
                        by));
            }
        }

        using var assets =
            new MapAssetService();

        var service =
            new MapVisualRegistrationService(
                assets);

        var registration =
            service.RegisterLocal(
                "test",
                baseMap,
                screenshot);

        Assert.NotNull(
            registration);

        Assert.True(
            registration!.Confidence >
            0.30);

        var centerExpected =
            new MapPoint(
                0.50 *
                MapPoint.MapSize,
                0.50 *
                MapPoint.MapSize);

        var centerActual =
            registration.ScreenPixelToWorld(
                screenshot.Width / 2.0,
                screenshot.Height / 2.0,
                screenshot.Width,
                screenshot.Height);

        Assert.True(
            centerActual.DistanceMeters(
                centerExpected) <
            900);

        var angleDelta =
            (
                registration.RotationDeg -
                expected.RotationDeg
            ) %
            360;

        if (angleDelta > 180)
            angleDelta -= 360;

        if (angleDelta < -180)
            angleDelta += 360;

        var angleError =
            Math.Abs(
                angleDelta);

        Assert.True(
            angleError <= 15,
            "rotation error was " +
            angleError.ToString("F1") +
            "°");

        Assert.InRange(
            registration.Width01,
            0.34,
            0.58);
    }

    [Fact]
    public void RoadGraphRouter_ReusesCorePathCacheAndInvalidatesOnRiskChange()
    {
        var graph =
            new RoadGraph
            {
                MapId = "cache-test",
                UpdatedUtc =
                    new DateTime(
                        2026,
                        9,
                        18,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc),
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
                                    12,
                                    10)
                        },
                        new()
                        {
                            Id = "c",
                            Position =
                                new MapPoint(
                                    14,
                                    10)
                        },
                        new()
                        {
                            Id = "d",
                            Position =
                                new MapPoint(
                                    16,
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
                            Class =
                                RoadClass.Primary,
                            Verified = true
                        },
                        new()
                        {
                            Id = "bc",
                            A = "b",
                            B = "c",
                            Class =
                                RoadClass.Primary,
                            Verified = true
                        },
                        new()
                        {
                            Id = "cd",
                            A = "c",
                            B = "d",
                            Class =
                                RoadClass.Primary,
                            Verified = true
                        }
                    }
            };

        var router =
            new RoadGraphRouter();

        var start =
            new MapPoint(
                10.1,
                10);

        var end =
            new MapPoint(
                15.9,
                10);

        var first =
            router.TryPlan(
                graph,
                start,
                end,
                RoutePreference.Fastest,
                VehicleRoutingProfileService.Generic());

        Assert.NotNull(first);

        var missesAfterFirst =
            router.PathCacheMisses;

        var second =
            router.TryPlan(
                graph,
                start,
                end,
                RoutePreference.Fastest,
                VehicleRoutingProfileService.Generic());

        Assert.NotNull(second);

        Assert.True(
            router.PathCacheHits > 0);

        Assert.True(
            router.TopologyCacheHits > 0);

        var missesBeforeRisk =
            router.PathCacheMisses;

        var visualRisk =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["bc"] = 0.95
            };

        var third =
            router.TryPlan(
                graph,
                start,
                end,
                RoutePreference.Fastest,
                VehicleRoutingProfileService.Generic(),
                Array.Empty<NavigationHazard>(),
                visualRisk);

        Assert.NotNull(third);

        Assert.True(
            router.PathCacheMisses >
            missesBeforeRisk);

        Assert.True(
            missesAfterFirst > 0);
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

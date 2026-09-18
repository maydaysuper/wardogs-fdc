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

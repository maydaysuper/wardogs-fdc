using System.Text.Json;

namespace WardogsNavigator.Services;

/// <summary>
/// Deterministic logistics optimizer. It deliberately does not call RoutePlanner or AI.
/// Economy decides WHAT/WHERE; navigation decides HOW to get there.
/// </summary>
public sealed class EconomyEngine
{
    public const double PalletBuy = 400;
    public const double PalletDrop = 2500;
    public const double PalletUnload = 1800;
    public const double PassengerDropGround = 375;
    public const double PassengerDropAir = 750;
    public const double PassengerSurvive = 500;
    public const double FuelUsdPerL = 1;
    public const double PalletHandleMinutes = 0.8;
    public const double PassengerHandleMinutes = 0.2;
    public const double RefuelMinutes = 4;

    private readonly List<VehicleSpec> _vehicles;

    public EconomyEngine(string? dataRoot = null)
    {
        var root = dataRoot ?? Path.Combine(AppContext.BaseDirectory, "Data");
        var path = Path.Combine(root, "vehicles.json");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _vehicles = File.Exists(path)
            ? JsonSerializer.Deserialize<List<VehicleSpec>>(File.ReadAllText(path), opts) ?? new List<VehicleSpec>()
            : new List<VehicleSpec>();
    }

    public EconomyEngine(IEnumerable<VehicleSpec> vehicles) => _vehicles = vehicles.ToList();

    public IReadOnlyList<VehicleSpec> Vehicles => _vehicles;

    public List<EconomicPlan> Optimize(
        MapPoint origin,
        IEnumerable<Destination> destinations,
        int trips = 3,
        bool unloadAtFob = true,
        bool expectSurvive = true)
    {
        trips = Math.Max(1, trips);
        var plans = new List<EconomicPlan>();

        foreach (var vehicle in _vehicles)
        {
            foreach (var destination in destinations)
            {
                var distanceKm = origin.DistanceKm(destination.Position);
                if (distanceKm < 0.05) continue;

                foreach (var mode in ModesFor(vehicle))
                foreach (var roundTrip in new[] { true, false })
                {
                    var load = LoadFor(vehicle, mode);
                    if (load.Pallets + load.Passengers == 0) continue;

                    var first = ScoreTrip(vehicle, destination.Kind, distanceKm, load.Pallets, load.Passengers,
                        roundTrip, false, unloadAtFob, expectSurvive);
                    var rest = ScoreTrip(vehicle, destination.Kind, distanceKm, load.Pallets, load.Passengers,
                        roundTrip, roundTrip, unloadAtFob, expectSurvive);

                    var sessionNet = first.Net + rest.Net * (trips - 1);
                    var sessionMinutes = first.Minutes + rest.Minutes * (trips - 1);

                    plans.Add(new EconomicPlan
                    {
                        Vehicle = vehicle,
                        Destination = destination,
                        LoadMode = mode,
                        RoundTrip = roundTrip,
                        Pallets = load.Pallets,
                        Passengers = load.Passengers,
                        DirectDistanceKm = distanceKm,
                        EstimatedMinutes = first.Minutes,
                        FirstTripNet = first.Net,
                        SessionNet = sessionNet,
                        SessionPerMinute = sessionMinutes > 0.05 ? sessionNet / sessionMinutes : sessionNet,
                        BreakEvenTrips = first.BreakEvenTrips
                    });
                }
            }
        }

        return plans
            .OrderByDescending(p => p.SessionNet)
            .ThenByDescending(p => p.SessionPerMinute)
            .ToList();
    }

    public EconomicPlan? PickBest(IEnumerable<EconomicPlan> plans)
    {
        var list = plans.ToList();
        var cargo = list.Where(p => p.Pallets > 0).ToList();
        return (cargo.Count > 0 ? cargo : list).FirstOrDefault();
    }

    private static IEnumerable<LoadMode> ModesFor(VehicleSpec v)
    {
        if (v.PalletSlots > 0) yield return LoadMode.Cargo;
        if (v.Passengers > 0) yield return LoadMode.Taxi;
        if (v.PalletSlots > 0 && v.Passengers > 0) yield return LoadMode.Mixed;
    }

    private static (int Pallets, int Passengers) LoadFor(VehicleSpec v, LoadMode mode) => mode switch
    {
        LoadMode.Cargo => (v.PalletSlots, 0),
        LoadMode.Taxi => (0, v.Passengers),
        _ => (v.PalletSlots, Math.Min(v.Passengers, 4))
    };

    private static TripScore ScoreTrip(
        VehicleSpec v,
        DestinationKind kind,
        double distanceKm,
        int pallets,
        int passengers,
        bool roundTrip,
        bool ownedVehicle,
        bool unloadAtFob,
        bool expectSurvive)
    {
        var driveKm = roundTrip ? distanceKm * 2 : distanceKm;
        var tanks = v.RangeKm <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(driveKm / v.RangeKm - 1e-9));
        var refuels = Math.Max(0, tanks - 1);
        var driveMinutes = v.SpeedKmh > 0 ? driveKm / v.SpeedKmh * 60.0 : 0;
        var handling = pallets * PalletHandleMinutes + passengers * PassengerHandleMinutes;
        var minutes = driveMinutes + handling + refuels * RefuelMinutes;

        var spawn = ownedVehicle ? 0 : v.Price;
        var fuelPerKm = v.RangeKm > 0 ? v.FuelL / v.RangeKm * FuelUsdPerL : 0;
        var fuel = fuelPerKm * driveKm;
        var palletCost = pallets * PalletBuy;

        var paid = kind != DestinationKind.Field;
        var palletGross = paid ? pallets * PalletDrop : 0;
        if (kind == DestinationKind.Fob && unloadAtFob)
            palletGross += pallets * PalletUnload;

        var passengerGross = paid ? passengers * (v.Air ? PassengerDropAir : PassengerDropGround) : 0;
        if (paid && expectSurvive) passengerGross += passengers * PassengerSurvive;

        var gross = palletGross + passengerGross;
        var net = gross - spawn - fuel - palletCost;
        var recurringNet = gross - fuel - palletCost;
        int? breakEven = spawn <= 0 ? 1 :
            recurringNet > 0 ? Math.Max(1, (int)Math.Ceiling(spawn / recurringNet)) : null;

        return new TripScore(net, minutes, breakEven);
    }

    private readonly record struct TripScore(double Net, double Minutes, int? BreakEvenTrips);
}

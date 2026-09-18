namespace WardogsNavigator.Services;

public static class VehicleRoutingProfileService
{
    public static VehicleRoutingProfile For(VehicleSpec? vehicle)
    {
        if (vehicle == null)
            return Generic();

        if (vehicle.Air)
        {
            return new VehicleRoutingProfile
            {
                VehicleId = vehicle.Id,
                Label = vehicle.NameZh,
                PrimaryFactor = 1,
                SecondaryFactor = 1,
                TrackFactor = 1,
                BridgeFactor = 1,
                RiskTolerance = 0.85
            };
        }

        return vehicle.Id.ToLowerInvariant() switch
        {
            "bobcat" => new VehicleRoutingProfile
            {
                VehicleId = vehicle.Id,
                Label = vehicle.NameZh,
                PrimaryFactor = 0.92,
                SecondaryFactor = 0.90,
                TrackFactor = 0.82,
                BridgeFactor = 0.90,
                RiskTolerance = 0.72
            },
            "dune-buggy" => new VehicleRoutingProfile
            {
                VehicleId = vehicle.Id,
                Label = vehicle.NameZh,
                PrimaryFactor = 1.00,
                SecondaryFactor = 0.88,
                TrackFactor = 0.82,
                BridgeFactor = 0.92,
                RiskTolerance = 0.70
            },
            "kodiak" => new VehicleRoutingProfile
            {
                VehicleId = vehicle.Id,
                Label = vehicle.NameZh,
                PrimaryFactor = 1.00,
                SecondaryFactor = 0.84,
                TrackFactor = 0.68,
                BridgeFactor = 0.94,
                RiskTolerance = 0.62
            },
            "ural" or "ural-defender" or "ural-defender-m249" => new VehicleRoutingProfile
            {
                VehicleId = vehicle.Id,
                Label = vehicle.NameZh,
                PrimaryFactor = 0.96,
                SecondaryFactor = 0.75,
                TrackFactor = 0.52,
                BridgeFactor = 0.78,
                RiskTolerance = vehicle.Armed ? 0.58 : 0.42
            },
            _ => Generic(vehicle.Id, vehicle.NameZh)
        };
    }

    public static VehicleRoutingProfile Generic(
        string vehicleId = "generic-ground",
        string label = "通用地面车辆") =>
        new()
        {
            VehicleId = vehicleId,
            Label = label,
            PrimaryFactor = 1.0,
            SecondaryFactor = 0.82,
            TrackFactor = 0.58,
            BridgeFactor = 0.90,
            RiskTolerance = 0.50
        };
}

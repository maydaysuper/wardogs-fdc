namespace WardogsNavigator.Services;

public static class FireControl
{
    public static FireSolution Calculate(MapPoint from, MapPoint to)
    {
        var az = from.BearingDegTo(to);
        return new FireSolution
        {
            DistanceMeters = from.DistanceMeters(to),
            AzimuthDeg = az,
            DirectionMils = NormalizeMils(az / 360.0 * 6400.0)
        };
    }

    public static double NormalizeMils(double mils)
    {
        mils %= 6400.0;
        if (mils < 0) mils += 6400.0;
        return mils;
    }
}

namespace PlateBack.Application.Features.Listings;

/// <summary>Distance helpers for radius search (design doc 4.5).</summary>
public static class Geo
{
    private const double EarthRadiusKm = 6371;
    private const double KmPerDegreeLatitude = 111.32;

    /// <summary>Great-circle distance in kilometres (Haversine formula).</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double ToRadians(double degrees) => degrees * Math.PI / 180;
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return EarthRadiusKm * 2 * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>
    /// A latitude/longitude box that contains the circle. Used as a cheap
    /// prefilter in SQL (it can use an index); the exact distance is then
    /// computed for the few rows inside the box.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLng, double MaxLng) BoundingBox(double lat, double lng, double radiusKm)
    {
        var latDelta = radiusKm / KmPerDegreeLatitude;
        var lngDelta = radiusKm / (KmPerDegreeLatitude * Math.Max(0.01, Math.Cos(lat * Math.PI / 180)));
        return (lat - latDelta, lat + latDelta, lng - lngDelta, lng + lngDelta);
    }
}

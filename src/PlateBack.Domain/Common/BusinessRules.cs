namespace PlateBack.Domain.Common;

/// <summary>
/// Business-rule values from design doc 5.6. Bound from the "BusinessRules"
/// configuration section so they can be tuned without changing code.
/// </summary>
public sealed class BusinessRules
{
    /// <summary>Units one user may reserve on one listing.</summary>
    public int MaxPerListing { get; set; } = 2;

    /// <summary>Active reservations a user may hold at once.</summary>
    public int MaxActiveReservations { get; set; } = 3;

    /// <summary>Active reservations allowed when reliability is low.</summary>
    public int MaxActiveReservationsLowReliability { get; set; } = 1;

    /// <summary>Reliability below this value counts as low.</summary>
    public int LowReliabilityBelow { get; set; } = 60;

    public int CollectBonus { get; set; } = 2;
    public int LateCancelPenalty { get; set; } = 5;
    public int NoShowPenalty { get; set; } = 20;

    public int ListingQuantityMax { get; set; } = 200;
    public TimeSpan PickupWindowMin { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan PickupWindowMax { get; set; } = TimeSpan.FromHours(12);

    public double SearchRadiusDefaultKm { get; set; } = 5;
    public double SearchRadiusMaxKm { get; set; } = 25;
}

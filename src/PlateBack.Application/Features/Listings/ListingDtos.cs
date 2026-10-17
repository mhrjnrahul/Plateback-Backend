using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Listings;

public sealed record OrganizationSummaryDto(Guid Id, string Name, bool IsVerified);

/// <summary>A listing in search results and cards.</summary>
public sealed record ListingSummaryDto(
    Guid Id,
    string Title,
    ListingCategory Category,
    OrganizationSummaryDto Organization,
    int Price,
    int OriginalPrice,
    int QuantityTotal,
    int QuantityAvailable,
    DateTimeOffset PickupStart,
    DateTimeOffset PickupEnd,
    ListingStatus Status,
    double? DistanceKm,
    string? ImageUrl)
{
    public static ListingSummaryDto From(Listing l, Organization o, double? distanceKm = null) =>
        new(l.Id, l.Title, l.Category, new OrganizationSummaryDto(o.Id, o.Name, o.Status == OrganizationStatus.Verified),
            l.Price, l.OriginalPrice, l.QuantityTotal, l.QuantityAvailable, l.PickupStart, l.PickupEnd, l.Status,
            distanceKm is null ? null : Math.Round(distanceKm.Value, 1), l.ImageUrl);
}

/// <summary>GET /listings/{id}</summary>
public sealed record ListingDetailDto(
    Guid Id,
    string Title,
    ListingCategory Category,
    OrganizationSummaryDto Organization,
    int Price,
    int OriginalPrice,
    int QuantityTotal,
    int QuantityAvailable,
    DateTimeOffset PickupStart,
    DateTimeOffset PickupEnd,
    ListingStatus Status,
    double? DistanceKm,
    string? ImageUrl,
    string Description,
    string AllergenNotes,
    string Address,
    double Latitude,
    double Longitude,
    int MaxPerUser)
{
    public static ListingDetailDto From(Listing l, Organization o, int maxPerUser) =>
        new(l.Id, l.Title, l.Category, new OrganizationSummaryDto(o.Id, o.Name, o.Status == OrganizationStatus.Verified),
            l.Price, l.OriginalPrice, l.QuantityTotal, l.QuantityAvailable, l.PickupStart, l.PickupEnd, l.Status,
            null, l.ImageUrl, l.Description, l.AllergenNotes, l.Address, l.Latitude, l.Longitude, maxPerUser);
}

/// <summary>Body of POST /organizations/{orgId}/listings and PUT /listings/{id}.</summary>
public sealed record ListingInput(
    string Title,
    string Category,
    string? Description,
    int? QuantityTotal,
    int? OriginalPrice,
    int? Price,
    DateTimeOffset? PickupStart,
    DateTimeOffset? PickupEnd,
    string Address,
    double? Latitude,
    double? Longitude,
    string AllergenNotes,
    string? ImageUrl);

/// <summary>Query parameters of GET /listings.</summary>
public sealed record SearchListingsQuery(
    double? Lat,
    double? Lng,
    double? RadiusKm,
    string? Category,
    int? MaxPrice,
    DateTimeOffset? PickupFrom,
    DateTimeOffset? PickupTo,
    string? Q,
    string? Sort,
    int? Page,
    int? PageSize);

/// <summary>GET /stats/impact</summary>
public sealed record ImpactStatsDto(int MealsRescued, int PartnerBusinesses, int ActiveListings);

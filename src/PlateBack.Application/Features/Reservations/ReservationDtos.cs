using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Reservations;

public sealed record CreateReservationRequest(int Quantity);

public sealed record VerifyPickupRequest(string PickupCode);

public sealed record ReservationDto(
    Guid Id,
    Guid ListingId,
    int Quantity,
    ReservationStatus Status,
    string PickupCode,
    DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? CancelledAt,
    bool LateCancel)
{
    public static ReservationDto From(Reservation r) =>
        new(r.Id, r.ListingId, r.Quantity, r.Status, r.PickupCode, r.ReservedAt, r.ExpiresAt,
            r.CollectedAt, r.CancelledAt, r.LateCancel);
}

/// <summary>The listing details a customer needs next to a reservation.</summary>
public sealed record ReservationListingDto(
    string Title,
    ListingCategory Category,
    int Price,
    int OriginalPrice,
    DateTimeOffset PickupStart,
    DateTimeOffset PickupEnd,
    string Address,
    double Latitude,
    double Longitude,
    string? ImageUrl,
    Guid OrganizationId,
    string OrganizationName);

/// <summary>A reservation on My reservations.</summary>
public sealed record ReservationWithListingDto(
    Guid Id,
    Guid ListingId,
    int Quantity,
    ReservationStatus Status,
    string PickupCode,
    DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? CancelledAt,
    bool LateCancel,
    ReservationListingDto Listing)
{
    public static ReservationWithListingDto From(Reservation r, Listing l, Organization o) =>
        new(r.Id, r.ListingId, r.Quantity, r.Status, r.PickupCode, r.ReservedAt, r.ExpiresAt,
            r.CollectedAt, r.CancelledAt, r.LateCancel,
            new ReservationListingDto(l.Title, l.Category, l.Price, l.OriginalPrice, l.PickupStart, l.PickupEnd,
                l.Address, l.Latitude, l.Longitude, l.ImageUrl, o.Id, o.Name));
}

/// <summary>A reservation in the business's counter queue.</summary>
public sealed record OrganizationReservationDto(
    Guid Id,
    Guid ListingId,
    int Quantity,
    ReservationStatus Status,
    string PickupCode,
    DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? CancelledAt,
    bool LateCancel,
    string CustomerName,
    string ListingTitle)
{
    public static OrganizationReservationDto From(Reservation r, string customerName, string listingTitle) =>
        new(r.Id, r.ListingId, r.Quantity, r.Status, r.PickupCode, r.ReservedAt, r.ExpiresAt,
            r.CollectedAt, r.CancelledAt, r.LateCancel, customerName, listingTitle);
}

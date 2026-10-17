using PlateBack.Domain.Common;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Entities;

/// <summary>
/// A customer's claim on part of a listing. Lifecycle (design doc 2.5):
/// Reserved to Collected, Cancelled or NoShow; all three are final.
/// </summary>
public sealed class Reservation : Entity
{
    private Reservation() { }

    public Guid ListingId { get; private set; }
    public Guid UserId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }

    /// <summary>Unique, shown as "K7M2-9QXA".</summary>
    public string PickupCode { get; private set; } = "";

    public DateTimeOffset ReservedAt { get; private set; }

    /// <summary>End of the pickup window; still Reserved after it means NoShow.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? CollectedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Cancelled after the pickup window opened (-5 reliability).</summary>
    public bool LateCancel { get; private set; }

    /// <summary>Soft reference to the staff member who handed the food over.</summary>
    public Guid? VerifiedBy { get; private set; }

    public static Reservation Create(Listing listing, Guid userId, int quantity, string pickupCode, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ListingId = listing.Id,
            UserId = userId,
            Quantity = quantity,
            Status = ReservationStatus.Reserved,
            PickupCode = pickupCode,
            ReservedAt = now,
            ExpiresAt = listing.PickupEnd,
        };

    /// <summary>
    /// The customer cancels. Returns true when it is a late cancellation
    /// (the pickup window had already opened).
    /// </summary>
    public bool Cancel(DateTimeOffset now, DateTimeOffset pickupStart)
    {
        EnsureReserved(ErrorCodes.AlreadyCollected, "This reservation was already collected.");
        if (now >= ExpiresAt)
        {
            // Too late to cancel: the window is over, so it counts as a no-show.
            throw new DomainException(ErrorCodes.WindowClosed, "The pickup window has ended.");
        }
        Status = ReservationStatus.Cancelled;
        CancelledAt = now;
        LateCancel = now >= pickupStart;
        return LateCancel;
    }

    /// <summary>The business cancelled the listing; never a penalty for the customer.</summary>
    public void CancelByBusiness(DateTimeOffset now)
    {
        EnsureReserved(ErrorCodes.AlreadyCollected, "This reservation was already collected.");
        Status = ReservationStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>Staff verified the pickup code at the counter.</summary>
    public void Collect(Guid staffId, DateTimeOffset now)
    {
        EnsureReserved(ErrorCodes.CodeAlreadyUsed, "This code was already used for a pickup.");
        Status = ReservationStatus.Collected;
        CollectedAt = now;
        VerifiedBy = staffId;
    }

    /// <summary>Used by the expiry job when the window ended without a pickup.</summary>
    public bool TryMarkNoShow(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Reserved || now < ExpiresAt)
        {
            return false;
        }
        Status = ReservationStatus.NoShow;
        return true;
    }

    /// <summary>Only for seeding demo history.</summary>
    public static Reservation Seed(
        Guid id, Listing listing, Guid userId, int quantity, string pickupCode, ReservationStatus status,
        DateTimeOffset reservedAt, DateTimeOffset? collectedAt, DateTimeOffset? cancelledAt) =>
        new()
        {
            Id = id,
            ListingId = listing.Id,
            UserId = userId,
            Quantity = quantity,
            Status = status,
            PickupCode = pickupCode,
            ReservedAt = reservedAt,
            ExpiresAt = listing.PickupEnd,
            CollectedAt = collectedAt,
            CancelledAt = cancelledAt,
        };

    private void EnsureReserved(string codeWhenCollected, string messageWhenCollected)
    {
        switch (Status)
        {
            case ReservationStatus.Reserved:
                return;
            case ReservationStatus.Collected:
                throw new DomainException(codeWhenCollected, messageWhenCollected);
            case ReservationStatus.NoShow:
                throw new DomainException(ErrorCodes.WindowClosed, "This reservation was not collected in time.");
            default:
                throw new DomainException(ErrorCodes.WindowClosed, "This reservation was cancelled.");
        }
    }
}

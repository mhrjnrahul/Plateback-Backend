using PlateBack.Domain.Common;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Entities;

/// <summary>What a business enters when it creates or edits a listing.</summary>
public sealed record ListingDetails(
    string Title,
    ListingCategory Category,
    string Description,
    int QuantityTotal,
    int OriginalPrice,
    int Price,
    DateTimeOffset PickupStart,
    DateTimeOffset PickupEnd,
    string Address,
    double Latitude,
    double Longitude,
    string AllergenNotes,
    string? ImageUrl);

/// <summary>
/// A batch of surplus food offered for pickup. Lifecycle (design doc 2.5):
/// Active to SoldOut when stock reaches 0 and back when stock returns;
/// Active or SoldOut to Expired when the window ends, or to Cancelled by the
/// business or an admin. Expired and Cancelled are final.
/// </summary>
/// <remarks>
/// Stock itself is changed with a single conditional SQL UPDATE (design doc
/// 5.5) so two customers can never take the same unit. The methods here hold
/// the rules; the handlers apply them atomically.
/// </remarks>
public sealed class Listing : Entity
{
    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 80;

    private Listing() { }

    public Guid OrganizationId { get; private set; }
    public string Title { get; private set; } = "";
    public ListingCategory Category { get; private set; }
    public string Description { get; private set; } = "";
    public string AllergenNotes { get; private set; } = "";

    /// <summary>Selling price in whole rupees; 0 means free.</summary>
    public int Price { get; private set; }

    /// <summary>The usual price (MRP) in whole rupees.</summary>
    public int OriginalPrice { get; private set; }

    public int QuantityTotal { get; private set; }
    public int QuantityAvailable { get; private set; }
    public DateTimeOffset PickupStart { get; private set; }
    public DateTimeOffset PickupEnd { get; private set; }
    public string Address { get; private set; } = "";
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public ListingStatus Status { get; private set; }
    public string? ImageUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Set when an admin removes the listing.</summary>
    public string? RemovalReason { get; private set; }

    public int QuantityReserved => QuantityTotal - QuantityAvailable;

    public static Listing Create(
        Guid organizationId, ListingDetails details, DateTimeOffset now, BusinessRules rules, Guid? id = null)
    {
        Validate(details, now, rules, minQuantity: 1);
        var listing = new Listing
        {
            Id = id ?? Guid.CreateVersion7(now),
            OrganizationId = organizationId,
            Status = ListingStatus.Active,
            CreatedAt = now,
        };
        listing.Apply(details);
        listing.QuantityAvailable = details.QuantityTotal;
        return listing;
    }

    /// <summary>Open for reservations: Active (not sold out) and the window has not ended.</summary>
    public bool IsOpen(DateTimeOffset now) => Status == ListingStatus.Active && now < PickupEnd;

    /// <summary>Still listed: Active or SoldOut, and the window has not ended.</summary>
    public bool IsLive(DateTimeOffset now) =>
        Status is ListingStatus.Active or ListingStatus.SoldOut && now < PickupEnd;

    /// <summary>
    /// Checks a reservation request against the listing and explains the
    /// failure. The final stock check happens again in the atomic UPDATE.
    /// </summary>
    public void EnsureCanReserve(int quantity, DateTimeOffset now)
    {
        if (Status is ListingStatus.Cancelled or ListingStatus.Expired || now >= PickupEnd)
        {
            throw new DomainException(ErrorCodes.WindowClosed, "This listing is no longer open for reservations.");
        }
        if (Status == ListingStatus.SoldOut || QuantityAvailable < quantity)
        {
            throw new DomainException(
                ErrorCodes.SoldOut,
                QuantityAvailable == 0
                    ? "This listing is sold out."
                    : $"Only {QuantityAvailable} left for this listing.");
        }
    }

    /// <summary>
    /// Edits a live listing. Quantity cannot drop below what is already
    /// reserved; stock is recalculated from the new total.
    /// </summary>
    public void Update(ListingDetails details, DateTimeOffset now, BusinessRules rules)
    {
        if (!IsLive(now))
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only live listings can be edited.");
        }
        var reserved = QuantityReserved;
        Validate(details, now, rules, minQuantity: Math.Max(1, reserved));
        Apply(details);
        QuantityAvailable = details.QuantityTotal - reserved;
        Status = QuantityAvailable == 0 ? ListingStatus.SoldOut : ListingStatus.Active;
    }

    /// <summary>The business cancels the listing; its reservations are cancelled by the handler.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is ListingStatus.Cancelled or ListingStatus.Expired)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only active listings can be cancelled.");
        }
        Status = ListingStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>An admin takes the listing down, with a reason.</summary>
    public void Remove(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ValidationError, "A reason is required.", "reason");
        }
        Cancel(now);
        RemovalReason = reason.Trim();
    }

    /// <summary>Used by the expiry job once the pickup window has ended.</summary>
    public bool TryExpire(DateTimeOffset now)
    {
        if (Status is not (ListingStatus.Active or ListingStatus.SoldOut) || now < PickupEnd)
        {
            return false;
        }
        Status = ListingStatus.Expired;
        return true;
    }

    /// <summary>Only for seeding demo data with stock already reserved.</summary>
    public void SetStockForSeed(int available, ListingStatus status)
    {
        QuantityAvailable = available;
        Status = status;
    }

    private void Apply(ListingDetails d)
    {
        Title = d.Title.Trim();
        Category = d.Category;
        Description = d.Description.Trim();
        QuantityTotal = d.QuantityTotal;
        OriginalPrice = d.OriginalPrice;
        Price = d.Price;
        PickupStart = d.PickupStart.ToUniversalTime();
        PickupEnd = d.PickupEnd.ToUniversalTime();
        Address = d.Address.Trim();
        Latitude = d.Latitude;
        Longitude = d.Longitude;
        AllergenNotes = d.AllergenNotes.Trim();
        ImageUrl = string.IsNullOrWhiteSpace(d.ImageUrl) ? null : d.ImageUrl.Trim();
    }

    private static void Validate(ListingDetails d, DateTimeOffset now, BusinessRules rules, int minQuantity)
    {
        var title = d.Title.Trim();
        if (title.Length is < TitleMinLength or > TitleMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationError, "Title must be 3-80 characters.", "title");
        }
        if (d.QuantityTotal < minQuantity || d.QuantityTotal > rules.ListingQuantityMax)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                minQuantity > 1
                    ? $"Quantity must be {minQuantity}-{rules.ListingQuantityMax} ({minQuantity} already reserved)."
                    : $"Quantity must be 1-{rules.ListingQuantityMax}.",
                "quantityTotal");
        }
        if (d.Price < 0 || d.OriginalPrice < 0)
        {
            throw new DomainException(ErrorCodes.ValidationError, "Prices cannot be negative.", "price");
        }
        if (d.Price > d.OriginalPrice)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Selling price must be equal to or lower than MRP.",
                "price");
        }
        if (d.PickupEnd <= now)
        {
            throw new DomainException(ErrorCodes.ValidationError, "The pickup window must end in the future.", "pickupEnd");
        }
        var length = d.PickupEnd - d.PickupStart;
        if (length < rules.PickupWindowMin || length > rules.PickupWindowMax)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Pickup must last between 15 minutes and 12 hours.",
                "pickupEnd");
        }
        if (d.AllergenNotes.Trim().Length < 3)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Allergen information is required. Write \"None known\" if there are none.",
                "allergenNotes");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Application.Common.Paging;
using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Reservations;

/// <summary>
/// POST /listings/{id}/reservations, the critical path (design doc 4.4).
/// Everything happens in one transaction:
/// 1. lock the customer's row, so their own parallel requests queue up behind
///    each other and cannot both pass the limit checks;
/// 2. check the listing and the customer's limits for a clear error message;
/// 3. take the stock with one conditional UPDATE, which is what actually stops
///    two customers getting the last unit;
/// 4. insert the reservation, audit row and notifications.
/// </summary>
public sealed class ReserveListingHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    public async Task<ReservationDto> HandleAsync(Guid listingId, CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = time.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        await db.LockUserAsync(userId, cancellationToken);
        var user = await db.GetActiveUserAsync(userId, cancellationToken);
        if (user.Role == UserRole.Admin)
        {
            throw AppException.Forbidden("Admin accounts cannot reserve food.");
        }

        var row = await (
            from l in db.Listings.AsNoTracking()
            join o in db.Organizations.AsNoTracking() on l.OrganizationId equals o.Id
            where l.Id == listingId
            select new { Listing = l, Organization = o }).FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("Listing");
        var listing = row.Listing;
        listing.EnsureCanReserve(request.Quantity, now);

        var active = await db.Reservations
            .Where(r => r.UserId == userId && r.Status == ReservationStatus.Reserved)
            .Select(r => r.ListingId)
            .ToListAsync(cancellationToken);
        if (active.Contains(listingId))
        {
            throw AppException.Conflict(ErrorCodes.LimitReached, "Already reserved",
                "You already have a reservation for this listing.");
        }
        var limit = user.ActiveReservationLimit(rules.Value);
        if (active.Count >= limit)
        {
            throw AppException.Conflict(ErrorCodes.LimitReached, "Reservation limit reached",
                limit == rules.Value.MaxActiveReservationsLowReliability
                    ? $"Your reliability score is below {rules.Value.LowReliabilityBelow}, so you can hold {limit} active reservation at a time."
                    : $"You can hold up to {limit} active reservations at a time.");
        }

        if (!await db.TryTakeStockAsync(listingId, request.Quantity, now, cancellationToken))
        {
            throw AppException.Conflict(ErrorCodes.SoldOut, "No longer available",
                "Someone else reserved the last portions just before you.");
        }

        var reservation = Reservation.Create(listing, userId, request.Quantity, await NewPickupCodeAsync(cancellationToken), now);
        db.Reservations.Add(reservation);
        db.Audit(userId, "ReservationCreated", nameof(Reservation), reservation.Id, now, $"listing {listingId}, quantity {request.Quantity}");

        var reservationLink = new Dictionary<string, string> { ["reservationId"] = reservation.Id.ToString() };
        db.Notify(userId, NotificationType.ReservationConfirmed, "Reservation confirmed",
            $"{listing.Title} from {row.Organization.Name}. Show code {reservation.PickupCode} at pickup.", now, reservationLink);
        db.Notify(await db.MemberIdsAsync(listing.OrganizationId, cancellationToken), NotificationType.ReservationConfirmed,
            "New reservation", $"{user.DisplayName} reserved {request.Quantity} x {listing.Title}.", now,
            new(reservationLink) { ["organizationId"] = listing.OrganizationId.ToString() });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await live.ListingsChangedAsync([listingId]);
        await live.OrganizationChangedAsync(listing.OrganizationId);
        return ReservationDto.From(reservation);
    }

    /// <summary>Codes are random; the unique index is the final guard, this check avoids hitting it.</summary>
    private async Task<string> NewPickupCodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var code = PickupCode.Generate();
            if (!await db.Reservations.AnyAsync(r => r.PickupCode == code, cancellationToken))
            {
                return code;
            }
        }
    }
}

/// <summary>GET /reservations/mine?status=active|history</summary>
public sealed class GetMyReservationsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<PageResult<ReservationWithListingDto>> HandleAsync(
        string? status, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var history = status?.ToLowerInvariant() switch
        {
            null or "" or "active" => false,
            "history" => true,
            _ => throw AppException.Validation("status", "Use active or history."),
        };
        var rows =
            from r in db.Reservations.AsNoTracking()
            join l in db.Listings.AsNoTracking() on r.ListingId equals l.Id
            join o in db.Organizations.AsNoTracking() on l.OrganizationId equals o.Id
            where r.UserId == userId && (history ? r.Status != ReservationStatus.Reserved : r.Status == ReservationStatus.Reserved)
            select new { Reservation = r, Listing = l, Organization = o };
        // Active: soonest pickup first. History: most recent first.
        rows = history
            ? rows.OrderByDescending(x => x.Reservation.ReservedAt)
            : rows.OrderBy(x => x.Listing.PickupStart);
        var result = await rows.ToPageAsync(PageRequest.From(page, pageSize), cancellationToken);
        return new PageResult<ReservationWithListingDto>(
            result.Items.Select(x => ReservationWithListingDto.From(x.Reservation, x.Listing, x.Organization)).ToList(),
            result.Page, result.PageSize, result.Total);
    }
}

/// <summary>
/// GET /reservations/{id}: the customer or a member of the listing's business.
/// Anyone else gets 404, so ids are not confirmed (design doc 6.4).
/// </summary>
public sealed class GetReservationHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<ReservationWithListingDto> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var row = await (
            from r in db.Reservations.AsNoTracking()
            join l in db.Listings.AsNoTracking() on r.ListingId equals l.Id
            join o in db.Organizations.AsNoTracking() on l.OrganizationId equals o.Id
            where r.Id == id
            select new { Reservation = r, Listing = l, Organization = o }).FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("Reservation");
        var allowed = row.Reservation.UserId == userId
            || await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == row.Organization.Id && m.UserId == userId, cancellationToken);
        return allowed
            ? ReservationWithListingDto.From(row.Reservation, row.Listing, row.Organization)
            : throw AppException.NotFound("Reservation");
    }
}

/// <summary>
/// POST /reservations/{id}/cancel: free before the window opens, a late
/// cancellation (-5 reliability) after. Stock goes back while the listing is open.
/// </summary>
public sealed class CancelReservationHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    public async Task<ReservationDto> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = time.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Reservation");
        var listing = await db.Listings.AsNoTracking().FirstAsync(l => l.Id == reservation.ListingId, cancellationToken);
        var user = await db.Users.FirstAsync(u => u.Id == userId, cancellationToken);

        if (reservation.Cancel(now, listing.PickupStart))
        {
            user.ApplyLateCancel(rules.Value);
        }
        db.Audit(userId, reservation.LateCancel ? "ReservationCancelledLate" : "ReservationCancelled",
            nameof(Reservation), reservation.Id, now);
        db.Notify(await db.MemberIdsAsync(listing.OrganizationId, cancellationToken), NotificationType.ReservationCancelled,
            "Reservation cancelled", $"{user.DisplayName} cancelled {reservation.Quantity} x {listing.Title}.", now,
            new() { ["reservationId"] = reservation.Id.ToString(), ["organizationId"] = listing.OrganizationId.ToString() });

        // Save first: the row version check means a second, parallel cancel of
        // the same reservation fails here and never returns the stock twice.
        await db.SaveChangesAsync(cancellationToken);
        await db.ReturnStockAsync(listing.Id, reservation.Quantity, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await live.ListingsChangedAsync([listing.Id]);
        await live.OrganizationChangedAsync(listing.OrganizationId);
        return ReservationDto.From(reservation);
    }
}

/// <summary>
/// POST /reservations/verify: staff type the customer's code at the counter.
/// A code from another business fails exactly like an unknown code.
/// </summary>
public sealed class VerifyPickupHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    public async Task<OrganizationReservationDto> HandleAsync(VerifyPickupRequest request, CancellationToken cancellationToken)
    {
        var staffId = currentUser.RequireUserId();
        var notFound = new AppException(404, ErrorCodes.NotFound, "Code not found",
            "No reservation with this code for your business.");
        var code = PlateBack.Domain.Entities.PickupCode.Normalize(request.PickupCode) ?? throw notFound;

        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.PickupCode == code, cancellationToken)
            ?? throw notFound;
        var listing = await db.Listings.AsNoTracking().FirstAsync(l => l.Id == reservation.ListingId, cancellationToken);
        if (!await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == listing.OrganizationId && m.UserId == staffId, cancellationToken))
        {
            throw notFound;
        }

        var now = time.GetUtcNow();
        reservation.Collect(staffId, now);
        var customer = await db.Users.FirstAsync(u => u.Id == reservation.UserId, cancellationToken);
        customer.ApplyCollected(rules.Value);
        db.Audit(staffId, "ReservationCollected", nameof(Reservation), reservation.Id, now);
        db.Notify(customer.Id, NotificationType.ReservationCollected, "Thanks for rescuing food!",
            $"Collected: {listing.Title}.", now, new() { ["reservationId"] = reservation.Id.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        await live.OrganizationChangedAsync(listing.OrganizationId);
        return OrganizationReservationDto.From(reservation, customer.DisplayName, listing.Title);
    }
}

/// <summary>
/// GET /organizations/{orgId}/reservations: today's counter queue. Waiting
/// pickups first, then collected, no-shows and cancelled; soonest first.
/// </summary>
public sealed class GetOrganizationReservationsHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<PageResult<OrganizationReservationDto>> HandleAsync(
        Guid organizationId, Guid? listingId, string? status, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var organization = await db.GetForMemberAsync(organizationId, currentUser.RequireUserId(), cancellationToken);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(organization.TimeZone, out var z) ? z : TimeZoneInfo.Utc;
        var localNow = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        var startOfToday = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();

        var rows =
            from r in db.Reservations.AsNoTracking()
            join l in db.Listings.AsNoTracking() on r.ListingId equals l.Id
            join u in db.Users.AsNoTracking() on r.UserId equals u.Id
            where l.OrganizationId == organizationId && l.PickupEnd >= startOfToday
            select new { Reservation = r, l.Title, u.DisplayName };
        if (listingId is { } lid)
        {
            rows = rows.Where(x => x.Reservation.ListingId == lid);
        }
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<ReservationStatus>(status, ignoreCase: true, out var parsed))
            {
                throw AppException.Validation("status", "Unknown reservation status.");
            }
            rows = rows.Where(x => x.Reservation.Status == parsed);
        }
        var ordered = rows
            .OrderBy(x => x.Reservation.Status == ReservationStatus.Reserved ? 0
                : x.Reservation.Status == ReservationStatus.Collected ? 1
                : x.Reservation.Status == ReservationStatus.NoShow ? 2 : 3)
            .ThenBy(x => x.Reservation.ExpiresAt);
        var result = await ordered.ToPageAsync(PageRequest.From(page, pageSize), cancellationToken);
        return new PageResult<OrganizationReservationDto>(
            result.Items.Select(x => OrganizationReservationDto.From(x.Reservation, x.DisplayName, x.Title)).ToList(),
            result.Page, result.PageSize, result.Total);
    }
}

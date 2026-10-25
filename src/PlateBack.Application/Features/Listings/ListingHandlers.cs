using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Application.Common.Paging;
using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Listings;

/// <summary>
/// GET /listings: live listings from verified businesses, filtered, sorted and
/// paged. With a location, a bounding box narrows the rows in SQL and the exact
/// Haversine distance is computed for those rows.
/// </summary>
public sealed class SearchListingsHandler(IAppDbContext db, IOptions<BusinessRules> rules, TimeProvider time)
{
    public async Task<PageResult<ListingSummaryDto>> HandleAsync(SearchListingsQuery query, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var page = PageRequest.From(query.Page, query.PageSize);
        var hasLocation = query.Lat is not null && query.Lng is not null;
        var sort = query.Sort ?? (hasLocation ? "distance" : "pickup");

        var rows =
            from l in db.Listings.AsNoTracking()
            join o in db.Organizations.AsNoTracking() on l.OrganizationId equals o.Id
            where (l.Status == ListingStatus.Active || l.Status == ListingStatus.SoldOut)
                && l.PickupEnd > now
                && o.Status == OrganizationStatus.Verified
            select new { Listing = l, Organization = o };

        if (!string.IsNullOrEmpty(query.Category))
        {
            var category = Enum.Parse<ListingCategory>(query.Category, ignoreCase: true);
            rows = rows.Where(r => r.Listing.Category == category);
        }
        if (query.MaxPrice is { } maxPrice)
        {
            rows = rows.Where(r => r.Listing.Price <= maxPrice);
        }
        // The pickup window overlaps the requested time range
        if (query.PickupFrom is { } from)
        {
            rows = rows.Where(r => r.Listing.PickupEnd > from);
        }
        if (query.PickupTo is { } to)
        {
            rows = rows.Where(r => r.Listing.PickupStart < to);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLowerInvariant();
            var categories = Enum.GetValues<ListingCategory>()
                .Where(c => c.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
            rows = rows.Where(r =>
                r.Listing.Title.ToLower().Contains(q)
                || r.Organization.Name.ToLower().Contains(q)
                || categories.Contains(r.Listing.Category));
        }

        if (!hasLocation)
        {
            // Sold-out listings go last, then the chosen order.
            var ordered = rows.OrderBy(r => r.Listing.Status == ListingStatus.SoldOut ? 1 : 0);
            ordered = sort == "price"
                ? ordered.ThenBy(r => r.Listing.Price).ThenBy(r => r.Listing.PickupEnd)
                : ordered.ThenBy(r => r.Listing.PickupEnd);
            return await PageAsync(
                rows.Select(r => r.Listing.Id),
                ordered.ThenBy(r => r.Listing.Id).Select(r => new Hit(r.Listing.Id, null)),
                page, cancellationToken);
        }

        var lat = query.Lat!.Value;
        var lng = query.Lng!.Value;
        var radius = Math.Min(query.RadiusKm ?? rules.Value.SearchRadiusDefaultKm, rules.Value.SearchRadiusMaxKm);
        var box = Geo.BoundingBox(lat, lng, radius);

        // Geo.DistanceKm, written so EF Core can translate it to SQL: the
        // distance is computed, filtered and sorted on in PostgreSQL.
        const double Radians = Math.PI / 180;
        var cosLat = Math.Cos(lat * Radians);
        var located = rows
            .Where(r => r.Listing.Latitude >= box.MinLat && r.Listing.Latitude <= box.MaxLat
                && r.Listing.Longitude >= box.MinLng && r.Listing.Longitude <= box.MaxLng)
            .Select(r => new
            {
                r.Listing,
                Distance = 2 * Geo.EarthRadiusKm * Math.Asin(Math.Sqrt(
                    Math.Pow(Math.Sin((r.Listing.Latitude - lat) * Radians / 2), 2)
                    + cosLat * Math.Cos(r.Listing.Latitude * Radians)
                        * Math.Pow(Math.Sin((r.Listing.Longitude - lng) * Radians / 2), 2))),
            })
            .Where(r => r.Distance <= radius);

        var byStatus = located.OrderBy(r => r.Listing.Status == ListingStatus.SoldOut ? 1 : 0);
        var sorted = sort switch
        {
            "price" => byStatus.ThenBy(r => r.Listing.Price).ThenBy(r => r.Distance),
            "pickup" => byStatus.ThenBy(r => r.Listing.PickupEnd).ThenBy(r => r.Distance),
            _ => byStatus.ThenBy(r => r.Distance).ThenBy(r => r.Listing.PickupEnd),
        };
        return await PageAsync(
            located.Select(r => r.Listing.Id),
            sorted.ThenBy(r => r.Listing.Id).Select(r => new Hit(r.Listing.Id, r.Distance)),
            page, cancellationToken);
    }

    private sealed record Hit(Guid Id, double? Distance);

    /// <summary>
    /// Counts the matches and picks the page using only columns in the
    /// ix_listings_live covering index, so PostgreSQL never reads the table
    /// for the rows it skips. Only the page's listings are then loaded in full.
    /// (Week 5: before, every listing in the search area was loaded, about
    /// 6,000 rows at 25 km, to return 20.)
    /// </summary>
    private async Task<PageResult<ListingSummaryDto>> PageAsync(
        IQueryable<Guid> matches, IQueryable<Hit> ordered, PageRequest page, CancellationToken cancellationToken)
    {
        var total = await matches.CountAsync(cancellationToken);
        var hits = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var ids = hits.Select(h => h.Id).ToList();
        var loaded = await (
            from l in db.Listings.AsNoTracking()
            join o in db.Organizations.AsNoTracking() on l.OrganizationId equals o.Id
            where ids.Contains(l.Id)
            select new { Listing = l, Organization = o }).ToDictionaryAsync(r => r.Listing.Id, cancellationToken);
        var items = hits
            .Select(h => ListingSummaryDto.From(loaded[h.Id].Listing, loaded[h.Id].Organization, h.Distance))
            .ToList();
        return new PageResult<ListingSummaryDto>(items, page.Page, page.PageSize, total);
    }
}

/// <summary>GET /listings/{id}: public, including listings that have ended (for reservation history).</summary>
public sealed class GetListingHandler(IAppDbContext db, IOptions<BusinessRules> rules)
{
    public async Task<ListingDetailDto> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
            from l in db.Listings.AsNoTracking()
            join o in db.Organizations.AsNoTracking() on l.OrganizationId equals o.Id
            where l.Id == id
            select new { Listing = l, Organization = o }).FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("Listing");
        return ListingDetailDto.From(row.Listing, row.Organization, rules.Value.MaxPerListing);
    }
}

/// <summary>GET /organizations/{orgId}/listings: open listings first (soonest pickup), then past ones.</summary>
public sealed class GetOrganizationListingsHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<PageResult<ListingSummaryDto>> HandleAsync(
        Guid organizationId, string? status, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var organization = await db.GetForMemberAsync(organizationId, currentUser.RequireUserId(), cancellationToken);
        var query = db.Listings.Where(l => l.OrganizationId == organizationId);
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<ListingStatus>(status, ignoreCase: true, out var parsed))
            {
                throw AppException.Validation("status", "Unknown listing status.");
            }
            query = query.Where(l => l.Status == parsed);
        }
        var now = time.GetUtcNow();
        var listings = await query.ToListAsync(cancellationToken);
        return listings
            .OrderBy(l => l.PickupEnd > now ? 0 : 1)
            .ThenBy(l => l.PickupEnd > now ? l.PickupStart.UtcTicks : -l.PickupStart.UtcTicks)
            .Select(l => ListingSummaryDto.From(l, organization))
            .ToList()
            .ToPage(PageRequest.From(page, pageSize));
    }
}

/// <summary>POST /organizations/{orgId}/listings: verified businesses only.</summary>
public sealed class CreateListingHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    public async Task<ListingDetailDto> HandleAsync(Guid organizationId, ListingInput input, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await db.GetActiveUserAsync(userId, cancellationToken);
        var organization = await db.GetForMemberAsync(organizationId, userId, cancellationToken);
        if (!organization.CanPublish)
        {
            throw new AppException(403, ErrorCodes.OrgNotVerified, "Organization not verified",
                "Your business must be verified by an admin before it can publish listings.");
        }
        var now = time.GetUtcNow();
        var listing = Listing.Create(organization.Id, input.ToDetails(organization), now, rules.Value);
        db.Listings.Add(listing);
        db.Audit(userId, "ListingCreated", nameof(Listing), listing.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        await live.ListingsChangedAsync([listing.Id]);
        await live.OrganizationChangedAsync(organization.Id);
        return ListingDetailDto.From(listing, organization, rules.Value.MaxPerListing);
    }
}

/// <summary>PUT /listings/{id}: quantity cannot drop below what is already reserved.</summary>
public sealed class UpdateListingHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    public async Task<ListingDetailDto> HandleAsync(Guid id, ListingInput input, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Listing");
        var organization = await db.GetForMemberAsync(listing.OrganizationId, userId, cancellationToken);
        var now = time.GetUtcNow();
        listing.Update(input.ToDetails(organization), now, rules.Value);
        db.Audit(userId, "ListingUpdated", nameof(Listing), listing.Id, now);
        // A concurrent reservation changes the row version, so this save fails
        // with 409 instead of overwriting the new stock count.
        await db.SaveChangesAsync(cancellationToken);
        await live.ListingsChangedAsync([listing.Id]);
        await live.OrganizationChangedAsync(organization.Id);
        return ListingDetailDto.From(listing, organization, rules.Value.MaxPerListing);
    }
}

/// <summary>POST /listings/{id}/cancel: also cancels its reservations (no penalty) and tells the customers.</summary>
public sealed class CancelListingHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    public async Task<ListingDetailDto> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Listing");
        var organization = await db.GetForMemberAsync(listing.OrganizationId, userId, cancellationToken);
        var now = time.GetUtcNow();
        listing.Cancel(now);
        await ListingCancellation.CancelReservationsAsync(db, listing, organization.Name, now, cancellationToken);
        db.Audit(userId, "ListingCancelled", nameof(Listing), listing.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        await live.ListingsChangedAsync([listing.Id]);
        await live.OrganizationChangedAsync(organization.Id);
        return ListingDetailDto.From(listing, organization, rules.Value.MaxPerListing);
    }
}

/// <summary>Shared by business cancellation, admin removal and suspension.</summary>
public static class ListingCancellation
{
    public static async Task CancelReservationsAsync(
        IAppDbContext db, Listing listing, string organizationName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reservations = await db.Reservations
            .Where(r => r.ListingId == listing.Id && r.Status == ReservationStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            reservation.CancelByBusiness(now);
            db.Notify(reservation.UserId, NotificationType.ListingCancelled, "Reservation cancelled by business",
                $"{organizationName} cancelled {listing.Title}. Sorry about that.", now,
                new() { ["listingId"] = listing.Id.ToString(), ["reservationId"] = reservation.Id.ToString() });
        }
    }
}

/// <summary>GET /stats/impact: public numbers for the landing page.</summary>
public sealed class GetImpactStatsHandler(IAppDbContext db, TimeProvider time)
{
    public async Task<ImpactStatsDto> HandleAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var meals = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Collected)
            .SumAsync(r => (int?)r.Quantity, cancellationToken) ?? 0;
        var partners = await db.Organizations.CountAsync(o => o.Status == OrganizationStatus.Verified, cancellationToken);
        var active = await db.Listings.CountAsync(l => l.Status == ListingStatus.Active && l.PickupEnd > now, cancellationToken);
        return new ImpactStatsDto(meals, partners, active);
    }
}

internal static class ListingInputMapping
{
    /// <summary>Location defaults to the business's own coordinates.</summary>
    public static ListingDetails ToDetails(this ListingInput input, Organization organization) =>
        new(
            input.Title,
            Enum.Parse<ListingCategory>(input.Category, ignoreCase: true),
            input.Description ?? "",
            input.QuantityTotal!.Value,
            input.OriginalPrice!.Value,
            input.Price!.Value,
            input.PickupStart!.Value,
            input.PickupEnd!.Value,
            input.Address,
            input.Latitude ?? organization.Latitude,
            input.Longitude ?? organization.Longitude,
            input.AllergenNotes,
            input.ImageUrl);
}

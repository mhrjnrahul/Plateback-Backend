using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Domain.Common;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Jobs;

/// <summary>
/// Runs every minute (design doc 2.6). Listings past their window become
/// Expired; reservations still waiting become NoShow and the customer loses
/// 20 reliability. Rows are claimed with SKIP LOCKED and changed only if still
/// due, so running it twice, or on two servers, never applies a penalty twice.
/// </summary>
public sealed class ExpireListingsJob(IAppDbContext db, IOptions<BusinessRules> rules, ILiveUpdates live, TimeProvider time)
{
    private const int BatchSize = 200;

    /// <returns>How many listings expired and how many reservations became no-shows.</returns>
    public async Task<(int Expired, int NoShows)> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var listings = await db.ClaimDueListingsAsync(now, BatchSize, cancellationToken);
        List<Guid> expired = [];
        foreach (var listing in listings.Where(l => l.TryExpire(now)))
        {
            expired.Add(listing.Id);
            db.Audit(null, "ListingExpired", "Listing", listing.Id, now);
        }

        var reservations = await db.ClaimDueNoShowsAsync(now, BatchSize, cancellationToken);
        var userIds = reservations.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        List<Guid> noShowListings = [];
        foreach (var reservation in reservations.Where(r => r.TryMarkNoShow(now)))
        {
            noShowListings.Add(reservation.ListingId);
            users[reservation.UserId].ApplyNoShow(rules.Value);
            db.Audit(null, "ReservationNoShow", "Reservation", reservation.Id, now);
            db.Notify(reservation.UserId, NotificationType.ReservationNoShow, "Reservation not collected",
                $"The pickup window ended before the food was collected. Reliability -{rules.Value.NoShowPenalty}.",
                now, new() { ["reservationId"] = reservation.Id.ToString() });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await PushAsync(expired, noShowListings, cancellationToken);
        return (expired.Count, noShowListings.Count);
    }

    /// <summary>Browsers drop expired listings; the businesses involved refresh their queues.</summary>
    private async Task PushAsync(List<Guid> expired, List<Guid> noShowListings, CancellationToken cancellationToken)
    {
        if (expired.Count == 0 && noShowListings.Count == 0)
        {
            return;
        }
        await live.ListingsChangedAsync(expired);
        var listingIds = expired.Concat(noShowListings).Distinct().ToList();
        var organizationIds = await db.Listings
            .Where(l => listingIds.Contains(l.Id))
            .Select(l => l.OrganizationId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var organizationId in organizationIds)
        {
            await live.OrganizationChangedAsync(organizationId);
        }
    }
}

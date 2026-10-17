using FluentValidation;

using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Application.Common.Paging;
using PlateBack.Application.Features.Listings;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Admin;

/// <summary>GET /admin/organizations?status=: the review queue, oldest first.</summary>
public sealed class GetOrganizationsForReviewHandler(IAppDbContext db, IIdentityService identity)
{
    public async Task<PageResult<OrganizationReviewItemDto>> HandleAsync(
        string? status, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Organizations.AsNoTracking().Include(o => o.Members).AsQueryable();
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<OrganizationStatus>(status, ignoreCase: true, out var parsed))
            {
                throw AppException.Validation("status", "Unknown organization status.");
            }
            query = query.Where(o => o.Status == parsed);
        }
        var result = await query.OrderBy(o => o.CreatedAt).ToPageAsync(PageRequest.From(page, pageSize), cancellationToken);
        var items = await ToItemsAsync(result.Items, cancellationToken);
        return new PageResult<OrganizationReviewItemDto>(items, result.Page, result.PageSize, result.Total);
    }

    /// <summary>Adds each owner's name and email (members must be loaded).</summary>
    public async Task<IReadOnlyList<OrganizationReviewItemDto>> ToItemsAsync(
        IReadOnlyList<Organization> organizations, CancellationToken cancellationToken)
    {
        static Guid? OwnerOf(Organization o) =>
            o.Members.Where(m => m.Role == MemberRole.Owner).Select(m => (Guid?)m.UserId).FirstOrDefault();

        var ownerIds = organizations.Select(OwnerOf).OfType<Guid>().Distinct().ToList();
        var names = await db.Users.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var emails = await identity.GetEmailsAsync(ownerIds, cancellationToken);
        return organizations.Select(o =>
        {
            var ownerId = OwnerOf(o) ?? Guid.Empty;
            return new OrganizationReviewItemDto(o.Id, o.Name, o.Type, o.Address, o.Latitude, o.Longitude, o.TimeZone,
                o.Status, o.CreatedAt, o.VerifiedAt, o.RejectionReason, o.Phone,
                names.GetValueOrDefault(ownerId, ""), emails.GetValueOrDefault(ownerId, ""));
        }).ToList();
    }
}

/// <summary>
/// POST /admin/organizations/{id}/verify, /reject and /suspend. Reject and
/// suspend need a reason; suspending also cancels the business's live
/// listings. The owners are told either way.
/// </summary>
public sealed class ReviewOrganizationHandler(
    IAppDbContext db, ICurrentUser currentUser, GetOrganizationsForReviewHandler reviewItems, TimeProvider time)
{
    public Task<OrganizationReviewItemDto> VerifyAsync(Guid id, CancellationToken cancellationToken) =>
        DecideAsync(id, null, (o, adminId, now) => o.Verify(adminId, now), "OrganizationVerified", cancellationToken);

    public Task<OrganizationReviewItemDto> RejectAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, request.Reason, (o, _, _) => o.Reject(request.Reason ?? ""), "OrganizationRejected", cancellationToken);

    public Task<OrganizationReviewItemDto> SuspendAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, request.Reason, (o, _, _) => o.Suspend(request.Reason ?? ""), "OrganizationSuspended", cancellationToken);

    private async Task<OrganizationReviewItemDto> DecideAsync(
        Guid id, string? reason, Action<Organization, Guid, DateTimeOffset> decide, string action, CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = time.GetUtcNow();
        var organization = await db.Organizations.Include(o => o.Members).FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Organization");
        decide(organization, adminId, now);

        if (organization.Status == OrganizationStatus.Suspended)
        {
            var live = await db.Listings
                .Where(l => l.OrganizationId == id && (l.Status == ListingStatus.Active || l.Status == ListingStatus.SoldOut))
                .ToListAsync(cancellationToken);
            foreach (var listing in live)
            {
                listing.Cancel(now);
                await ListingCancellation.CancelReservationsAsync(db, listing, organization.Name, now, cancellationToken);
            }
        }

        db.Audit(adminId, action, nameof(Organization), organization.Id, now, reason?.Trim());
        var owners = organization.Members.Select(m => m.UserId);
        var link = new Dictionary<string, string> { ["organizationId"] = organization.Id.ToString() };
        if (organization.Status == OrganizationStatus.Verified)
        {
            db.Notify(owners, NotificationType.OrganizationVerified, "Your business is verified",
                $"{organization.Name} can now publish listings.", now, link);
        }
        else
        {
            var title = organization.Status == OrganizationStatus.Rejected ? "Business not approved" : "Business suspended";
            db.Notify(owners, NotificationType.OrganizationRejected, title, $"{organization.Name}: {organization.RejectionReason}", now, link);
        }
        await db.SaveChangesAsync(cancellationToken);
        return (await reviewItems.ToItemsAsync([organization], cancellationToken))[0];
    }
}

/// <summary>GET /admin/users?q=: search accounts by name or email.</summary>
public sealed class SearchUsersHandler(IAppDbContext db, IIdentityService identity)
{
    public async Task<PageResult<AdminUserDto>> HandleAsync(string? q, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            var byEmail = await identity.FindUserIdByEmailAsync(term, cancellationToken);
            query = query.Where(u => u.DisplayName.ToLower().Contains(term) || u.Id == byEmail);
        }
        var result = await query.OrderBy(u => u.DisplayName).ToPageAsync(PageRequest.From(page, pageSize), cancellationToken);
        var emails = await identity.GetEmailsAsync(result.Items.Select(u => u.Id).ToList(), cancellationToken);
        return new PageResult<AdminUserDto>(
            result.Items.Select(u => new AdminUserDto(u.Id, emails.GetValueOrDefault(u.Id, ""), u.DisplayName, u.Role,
                u.ReliabilityScore, u.IsSuspended, u.SuspensionReason, u.CreatedAt)).ToList(),
            result.Page, result.PageSize, result.Total);
    }
}

/// <summary>POST /admin/users/{id}/suspend and /restore. A suspended account cannot log in or reserve.</summary>
public sealed class SuspendUserHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task SuspendAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = time.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw AppException.NotFound("User");
        user.Suspend(request.Reason ?? "");
        // End their sessions; the access token they hold expires within 15 minutes.
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now);
        }
        db.Audit(adminId, "UserSuspended", nameof(User), id, now, request.Reason?.Trim());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw AppException.NotFound("User");
        user.Restore();
        db.Audit(adminId, "UserRestored", nameof(User), id, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>POST /admin/listings/{id}/remove: take a listing down with a reason.</summary>
public sealed class RemoveListingHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task HandleAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = time.GetUtcNow();
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw AppException.NotFound("Listing");
        var organization = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == listing.OrganizationId, cancellationToken);
        listing.Remove(request.Reason ?? "", now);
        await ListingCancellation.CancelReservationsAsync(db, listing, organization.Name, now, cancellationToken);
        db.Audit(adminId, "ListingRemoved", nameof(Listing), id, now, request.Reason?.Trim());
        db.Notify(await db.MemberIdsAsync(organization.Id, cancellationToken), NotificationType.ListingCancelled,
            "Listing removed by PlateBack", $"{listing.Title}: {listing.RemovalReason}", now,
            new() { ["listingId"] = id.ToString() });
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>GET /admin/audit-logs?entityType=&amp;entityId=&amp;actorId=: newest first.</summary>
public sealed class GetAuditLogsHandler(IAppDbContext db)
{
    public async Task<PageResult<AuditLogDto>> HandleAsync(
        string? entityType, Guid? entityId, Guid? actorId, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }
        if (entityId is { } eid)
        {
            query = query.Where(a => a.EntityId == eid);
        }
        if (actorId is { } aid)
        {
            query = query.Where(a => a.ActorId == aid);
        }
        return await query
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new AuditLogDto(a.Id, a.ActorId, a.Action, a.EntityType, a.EntityId, a.Details, a.OccurredAt))
            .ToPageAsync(PageRequest.From(page, pageSize), cancellationToken);
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

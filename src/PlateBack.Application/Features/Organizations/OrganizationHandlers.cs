using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Organizations;

/// <summary>POST /organizations: starts Pending; the caller becomes its Owner.</summary>
public sealed class CreateOrganizationHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<OrganizationDto> HandleAsync(OrganizationRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await db.GetActiveUserAsync(userId, cancellationToken);
        var now = time.GetUtcNow();

        var organization = Organization.Create(
            Guid.CreateVersion7(now), request.Name, Enum.Parse<BusinessType>(request.Type, ignoreCase: true),
            request.Address, request.Latitude!.Value, request.Longitude!.Value, request.Phone, userId, now);
        db.Organizations.Add(organization);
        db.Audit(userId, "OrganizationCreated", nameof(Organization), organization.Id, now);

        var admins = await db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).ToListAsync(cancellationToken);
        db.Notify(admins, NotificationType.OrganizationVerified, "New business to review",
            $"{organization.Name} is waiting for verification.", now,
            new() { ["organizationId"] = organization.Id.ToString() });

        await db.SaveChangesAsync(cancellationToken);
        return OrganizationDto.From(organization);
    }
}

/// <summary>GET /organizations/mine</summary>
public sealed class GetMyOrganizationsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<OrganizationDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var organizations = await db.Organizations
            .Where(o => o.Members.Any(m => m.UserId == userId))
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);
        return organizations.Select(OrganizationDto.From).ToList();
    }
}

/// <summary>GET /organizations/{id}: public when verified; otherwise only members and admins see it.</summary>
public sealed class GetOrganizationHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<OrganizationDto> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Organization");
        var userId = currentUser.UserId;
        var canSee = organization.Status == OrganizationStatus.Verified
            || currentUser.IsAdmin
            || (userId is { } uid && organization.IsMember(uid));
        // Hidden rather than forbidden, so ids of unverified businesses are not confirmed.
        return canSee ? OrganizationDto.From(organization) : throw AppException.NotFound("Organization");
    }
}

/// <summary>PATCH /organizations/{id}: owners only.</summary>
public sealed class UpdateOrganizationHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<OrganizationDto> HandleAsync(Guid id, OrganizationRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var organization = await db.GetForMemberAsync(id, userId, cancellationToken);
        if (!organization.IsOwner(userId))
        {
            throw AppException.Forbidden("Only an owner can edit the business details.");
        }
        organization.UpdateDetails(request.Name, Enum.Parse<BusinessType>(request.Type, ignoreCase: true),
            request.Address, request.Latitude!.Value, request.Longitude!.Value, request.Phone);
        db.Audit(userId, "OrganizationUpdated", nameof(Organization), organization.Id, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return OrganizationDto.From(organization);
    }
}

/// <summary>GET, POST and DELETE /organizations/{id}/members: the team, owners only.</summary>
public sealed class OrganizationMembersHandler(
    IAppDbContext db, ICurrentUser currentUser, IIdentityService identity, TimeProvider time)
{
    public async Task<IReadOnlyList<MemberDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        return await ReadMembersAsync(organizationId, cancellationToken);
    }

    public async Task<IReadOnlyList<MemberDto>> AddAsync(Guid organizationId, AddMemberRequest request, CancellationToken cancellationToken)
    {
        var (organization, ownerId) = await RequireOwnerAsync(organizationId, cancellationToken);
        var userId = await identity.FindUserIdByEmailAsync(request.Email.Trim(), cancellationToken)
            ?? throw AppException.Validation("email", "No PlateBack account uses this email.");
        var now = time.GetUtcNow();
        db.OrganizationMembers.Add(organization.AddStaff(userId, now));
        db.Audit(ownerId, "MemberAdded", nameof(Organization), organization.Id, now, userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await ReadMembersAsync(organizationId, cancellationToken);
    }

    public async Task RemoveAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var (organization, ownerId) = await RequireOwnerAsync(organizationId, cancellationToken);
        organization.RemoveMember(userId);
        db.Audit(ownerId, "MemberRemoved", nameof(Organization), organization.Id, time.GetUtcNow(), userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Organization Organization, Guid OwnerId)> RequireOwnerAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var organization = await db.GetForMemberAsync(organizationId, userId, cancellationToken);
        return organization.IsOwner(userId)
            ? (organization, userId)
            : throw AppException.Forbidden("Only an owner can manage the team.");
    }

    private async Task<IReadOnlyList<MemberDto>> ReadMembersAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var members = await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.UserId, u.DisplayName, m.Role, m.JoinedAt })
            .OrderBy(m => m.Role).ThenBy(m => m.DisplayName)
            .ToListAsync(cancellationToken);
        var emails = await identity.GetEmailsAsync(members.Select(m => m.UserId).ToList(), cancellationToken);
        return members
            .Select(m => new MemberDto(m.UserId, m.DisplayName, emails.GetValueOrDefault(m.UserId, ""), m.Role, m.JoinedAt))
            .ToList();
    }
}

/// <summary>
/// GET /organizations/{id}/stats: dashboard numbers. "Today" and "this month"
/// use the organization's time zone, not the server's.
/// </summary>
public sealed class GetOrganizationStatsHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<OrganizationStatsDto> HandleAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await db.GetForMemberAsync(organizationId, currentUser.RequireUserId(), cancellationToken);
        var now = time.GetUtcNow();
        var zone = FindZone(organization.TimeZone);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var firstDay = localToday.AddDays(-13);
        var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);
        var since = StartOfDay(firstDay < monthStart ? firstDay : monthStart, zone);

        var listings = db.Listings.Where(l => l.OrganizationId == organizationId);
        var activeListings = await listings.CountAsync(l => l.Status == ListingStatus.Active && l.PickupEnd > now, cancellationToken);
        var reservations = db.Reservations.Where(r => listings.Any(l => l.Id == r.ListingId));
        var portionsReserved = await reservations
            .Where(r => r.Status == ReservationStatus.Reserved)
            .SumAsync(r => (int?)r.Quantity, cancellationToken) ?? 0;
        var collected = await reservations
            .Where(r => r.Status == ReservationStatus.Collected && r.CollectedAt >= since)
            .Select(r => new { r.CollectedAt, r.Quantity })
            .ToListAsync(cancellationToken);

        var byDay = collected
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.CollectedAt!.Value, zone).DateTime))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        var series = Enumerable.Range(0, 14)
            .Select(i => firstDay.AddDays(i))
            .Select(d => new DayCount(d.ToString("yyyy-MM-dd"), byDay.GetValueOrDefault(d)))
            .ToList();

        return new OrganizationStatsDto(
            activeListings,
            portionsReserved,
            byDay.GetValueOrDefault(localToday),
            byDay.Where(kv => kv.Key >= monthStart).Sum(kv => kv.Value),
            series);
    }

    private static TimeZoneInfo FindZone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

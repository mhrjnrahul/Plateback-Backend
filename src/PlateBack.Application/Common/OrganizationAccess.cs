using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Common;

/// <summary>
/// Organization permissions are read from the database on every request, not
/// from the token, so a removed member loses access at once (design doc 4.6).
/// </summary>
public static class OrganizationAccess
{
    /// <summary>The organization with its members, if the caller belongs to it.</summary>
    public static async Task<Organization> GetForMemberAsync(
        this IAppDbContext db, Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw AppException.NotFound("Organization");
        return organization.IsMember(userId) ? organization : throw AppException.NotMember();
    }

    public static async Task<List<Guid>> MemberIdsAsync(
        this IAppDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    /// <summary>The caller's domain user; suspended accounts cannot act.</summary>
    public static async Task<User> GetActiveUserAsync(
        this IAppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.Unauthenticated();
        return user.IsSuspended ? throw AppException.Suspended() : user;
    }
}

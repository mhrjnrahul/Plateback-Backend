using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Users;

/// <summary>Builds the signed-in user's profile with their memberships.</summary>
public sealed class CurrentUserReader(IAppDbContext db, IIdentityService identity)
{
    public async Task<CurrentUserDto> ReadAsync(User user, CancellationToken cancellationToken)
    {
        var emails = await identity.GetEmailsAsync([user.Id], cancellationToken);
        var memberships = await db.OrganizationMembers
            .Where(m => m.UserId == user.Id)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new MembershipDto(o.Id, o.Name, m.Role))
            .ToListAsync(cancellationToken);
        return new CurrentUserDto(
            user.Id,
            emails.GetValueOrDefault(user.Id, ""),
            user.DisplayName,
            user.Role,
            user.ReliabilityScore,
            user.IsSuspended,
            memberships);
    }

    public async Task<CurrentUserDto> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.Unauthenticated();
        return await ReadAsync(user, cancellationToken);
    }
}

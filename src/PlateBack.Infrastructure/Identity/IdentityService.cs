using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Infrastructure.Persistence;

namespace PlateBack.Infrastructure.Identity;

/// <summary>Logins through ASP.NET Core Identity: hashing, lockout and unique emails.</summary>
internal sealed class IdentityService(UserManager<ApplicationUser> users, AppDbContext db) : IIdentityService
{
    public async Task<IReadOnlyDictionary<string, string[]>> CreateAsync(
        Guid userId, string email, string password, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser { Id = userId, UserName = email, Email = email };
        var result = await users.CreateAsync(user, password);
        if (result.Succeeded)
        {
            return new Dictionary<string, string[]>();
        }
        return result.Errors
            .Select(e => e.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) =>
                    ("email", "An account with this email already exists."),
                nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) =>
                    ("email", "Enter a valid email address."),
                _ when e.Code.StartsWith("Password", StringComparison.Ordinal) => ("password", e.Description),
                _ => ("email", e.Description),
            })
            .GroupBy(e => e.Item1)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Item2).Distinct().ToArray());
    }

    public async Task<(PasswordCheck Result, Guid UserId)> CheckPasswordAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            return (PasswordCheck.Invalid, Guid.Empty);
        }
        if (await users.IsLockedOutAsync(user))
        {
            return (PasswordCheck.LockedOut, user.Id);
        }
        if (!await users.CheckPasswordAsync(user, password))
        {
            // Counts towards lockout: 5 failures lock the account for 15 minutes.
            await users.AccessFailedAsync(user);
            return (await users.IsLockedOutAsync(user) ? PasswordCheck.LockedOut : PasswordCheck.Invalid, user.Id);
        }
        await users.ResetAccessFailedCountAsync(user);
        return (PasswordCheck.Success, user.Id);
    }

    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = users.NormalizeEmail(email);
        return await db.IdentityUsers
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await db.IdentityUsers
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? "", cancellationToken);
}

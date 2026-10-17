namespace PlateBack.Application.Common.Abstractions;

public enum PasswordCheck
{
    Success,
    Invalid,
    LockedOut,
}

/// <summary>
/// Login data handled by ASP.NET Core Identity (decision 3): email, password
/// hash and lockout. The business side of the user is the domain User.
/// </summary>
public interface IIdentityService
{
    /// <summary>Creates the login. Returns field errors (empty when it worked).</summary>
    Task<IReadOnlyDictionary<string, string[]>> CreateAsync(Guid userId, string email, string password, CancellationToken cancellationToken);

    /// <summary>Checks the password and counts failures towards lockout.</summary>
    Task<(PasswordCheck Result, Guid UserId)> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

using PlateBack.Application.Common.Errors;

namespace PlateBack.Application.Common.Abstractions;

/// <summary>Who is calling, read from the access token.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAdmin { get; }
}

public static class CurrentUserExtensions
{
    /// <summary>The caller's id, or a 401 when nobody is signed in.</summary>
    public static Guid RequireUserId(this ICurrentUser currentUser) =>
        currentUser.UserId ?? throw AppException.Unauthenticated();
}

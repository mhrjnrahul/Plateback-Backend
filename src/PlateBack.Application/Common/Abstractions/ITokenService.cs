using PlateBack.Domain.Entities;

namespace PlateBack.Application.Common.Abstractions;

/// <summary>Creates signed access tokens (JWT).</summary>
public interface ITokenService
{
    /// <summary>A token for the user, and how many seconds it is valid.</summary>
    (string Token, int ExpiresInSeconds) CreateAccessToken(User user, string email);
}

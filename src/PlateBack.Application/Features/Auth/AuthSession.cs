using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Options;
using PlateBack.Application.Features.Users;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Auth;

/// <summary>
/// Issues the token pair for a signed-in user: a short-lived JWT for the
/// Authorization header and a random refresh token stored only as a hash.
/// </summary>
public sealed class AuthSession(
    IAppDbContext db,
    ITokenService tokens,
    CurrentUserReader reader,
    IOptions<JwtOptions> options,
    TimeProvider time)
{
    /// <summary>Adds a new refresh token to the unit of work and builds the response. The caller saves.</summary>
    public async Task<(AuthResult Result, RefreshToken Stored)> StartAsync(User user, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var raw = NewRawToken();
        var stored = RefreshToken.Issue(user.Id, Hash(raw), now, TimeSpan.FromDays(options.Value.RefreshTokenDays));
        db.RefreshTokens.Add(stored);

        var profile = await reader.ReadAsync(user, cancellationToken);
        var (accessToken, expiresIn) = tokens.CreateAccessToken(user, profile.Email);
        return (new AuthResult(new AuthResponse(accessToken, expiresIn, profile), raw, stored.ExpiresAt), stored);
    }

    /// <summary>SHA-256 of the token, hex encoded. The database never sees the token itself.</summary>
    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>256 random bits, URL-safe base64.</summary>
    private static string NewRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using PlateBack.Application.Features.Users;

namespace PlateBack.Application.Features.Auth;

public sealed record RegisterRequest(string DisplayName, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

/// <summary>Body of /auth/login, /auth/register and /auth/refresh.</summary>
public sealed record AuthResponse(string AccessToken, int ExpiresIn, CurrentUserDto User);

/// <summary>
/// What a successful sign-in produces. The refresh token never goes in a
/// response body; the API puts it in an httpOnly cookie.
/// </summary>
public sealed record AuthResult(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

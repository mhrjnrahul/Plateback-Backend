using System.Security.Claims;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Options;
using PlateBack.Domain.Entities;

namespace PlateBack.Infrastructure.Identity;

/// <summary>
/// Access tokens: HMAC-SHA256 signed JWTs carrying the user id, email, name
/// and role. Organization memberships are deliberately not in the token
/// (design doc 4.6); they are checked in the database on each request.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time) : ITokenService
{
    public (string Token, int ExpiresInSeconds) CreateAccessToken(User user, string email)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(jwt.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
                new Claim("role", user.Role.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(jwt.Key), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }

    public static SymmetricSecurityKey SigningKey(string key) => new(Encoding.UTF8.GetBytes(key));
}

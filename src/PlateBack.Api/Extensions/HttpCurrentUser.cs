using Microsoft.IdentityModel.JsonWebTokens;

using PlateBack.Application.Common.Abstractions;

namespace PlateBack.Api.Extensions;

/// <summary>The caller, read from the validated access token's claims.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public bool IsAdmin => accessor.HttpContext?.User.IsInRole("Admin") ?? false;
}

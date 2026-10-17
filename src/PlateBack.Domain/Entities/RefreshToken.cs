using PlateBack.Domain.Common;

namespace PlateBack.Domain.Entities;

/// <summary>
/// A rotating refresh token (design doc 4.6). Only a SHA-256 hash of the token
/// is stored; the token itself only ever exists in the user's cookie.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The token that replaced this one when it was rotated.</summary>
    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>A revoked token that was replaced by rotation. Using it again means it was stolen.</summary>
    public bool WasRotated => RevokedAt is not null && ReplacedById is not null;

    public void Revoke(DateTimeOffset now, Guid? replacedById = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }
        RevokedAt = now;
        ReplacedById = replacedById;
    }
}

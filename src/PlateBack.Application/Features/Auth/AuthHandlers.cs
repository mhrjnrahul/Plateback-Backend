using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Application.Common.Options;
using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Auth;

/// <summary>POST /auth/register: a new customer account, signed in straight away.</summary>
public sealed class RegisterHandler(IAppDbContext db, IIdentityService identity, AuthSession session, TimeProvider time)
{
    public async Task<AuthResult> HandleAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var userId = Guid.CreateVersion7(now);

        // The Identity login and the domain user are created in one transaction (decision 3).
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var errors = await identity.CreateAsync(userId, request.Email.Trim(), request.Password, cancellationToken);
        if (errors.Count > 0)
        {
            throw new AppException(400, ErrorCodes.ValidationError, "Validation failed", null, errors);
        }
        var user = User.Create(userId, request.DisplayName, now);
        db.Users.Add(user);
        db.Audit(userId, "UserRegistered", nameof(User), userId, now);
        var (result, _) = await session.StartAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

/// <summary>POST /auth/login</summary>
public sealed class LoginHandler(IAppDbContext db, IIdentityService identity, AuthSession session)
{
    public async Task<AuthResult> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var (check, userId) = await identity.CheckPasswordAsync(request.Email.Trim(), request.Password, cancellationToken);
        switch (check)
        {
            case PasswordCheck.LockedOut:
                throw new AppException(
                    429, ErrorCodes.RateLimited, "Too many attempts",
                    "Too many failed logins. Try again in 15 minutes.");
            case PasswordCheck.Invalid:
                throw new AppException(401, ErrorCodes.Unauthenticated, "Login failed", "Email or password is incorrect.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.Unauthenticated();
        if (user.IsSuspended)
        {
            throw AppException.Suspended();
        }
        var (result, _) = await session.StartAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }
}

/// <summary>
/// POST /auth/refresh: rotates the refresh token. Presenting a token that was
/// already rotated means it was copied, so every session of that user ends
/// (reuse detection, design doc 4.6).
/// </summary>
public sealed class RefreshHandler(IAppDbContext db, AuthSession session, IOptions<JwtOptions> options, TimeProvider time)
{
    public async Task<AuthResult> HandleAsync(string? rawToken, CancellationToken cancellationToken)
    {
        var expired = AppException.Unauthenticated("Your session has ended. Please log in again.");
        if (string.IsNullOrEmpty(rawToken))
        {
            throw expired;
        }
        var now = time.GetUtcNow();
        var hash = AuthSession.Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw expired;

        if (token.WasRotated && now - token.RevokedAt < TimeSpan.FromSeconds(options.Value.RefreshReuseGraceSeconds))
        {
            // Two requests raced with the same cookie and the other one already
            // rotated it. The browser keeps the replacement, so answer with an
            // access token only and leave the cookie alone.
            var racer = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
            return racer is null || racer.IsSuspended ? throw expired : await session.AccessOnlyAsync(racer, cancellationToken);
        }
        if (token.WasRotated)
        {
            await RevokeAllAsync(token.UserId, now, cancellationToken);
            db.Audit(token.UserId, "RefreshTokenReused", nameof(RefreshToken), token.Id, now);
            await db.SaveChangesAsync(cancellationToken);
            throw expired;
        }
        if (!token.IsActive(now))
        {
            throw expired;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        if (user is null || user.IsSuspended)
        {
            token.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            throw expired;
        }

        var (result, replacement) = await session.StartAsync(user, cancellationToken);
        token.Revoke(now, replacement.Id);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in active)
        {
            t.Revoke(now);
        }
    }
}

/// <summary>POST /auth/logout: revokes the current refresh token.</summary>
public sealed class LogoutHandler(IAppDbContext db, TimeProvider time)
{
    public async Task HandleAsync(string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return;
        }
        var hash = AuthSession.Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is not null)
        {
            token.Revoke(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Users;

/// <summary>GET /users/me</summary>
public sealed class GetCurrentUserHandler(ICurrentUser currentUser, CurrentUserReader reader)
{
    public Task<CurrentUserDto> HandleAsync(CancellationToken cancellationToken) =>
        reader.ReadAsync(currentUser.RequireUserId(), cancellationToken);
}

/// <summary>PATCH /users/me: change the display name.</summary>
public sealed class UpdateProfileHandler(IAppDbContext db, ICurrentUser currentUser, CurrentUserReader reader)
{
    public async Task<CurrentUserDto> HandleAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.Unauthenticated();
        user.Rename(request.DisplayName);
        await db.SaveChangesAsync(cancellationToken);
        return await reader.ReadAsync(user, cancellationToken);
    }
}

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.DisplayName)
            .Must(n => n?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength)
            .WithMessage("Enter your name (2-60 characters).");
    }
}

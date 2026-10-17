using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Users;

public sealed record MembershipDto(Guid OrganizationId, string OrganizationName, MemberRole Role);

/// <summary>GET /users/me</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role,
    int ReliabilityScore,
    bool IsSuspended,
    IReadOnlyList<MembershipDto> Memberships);

public sealed record UpdateProfileRequest(string DisplayName);

using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Admin;

/// <summary>Body of reject, suspend and remove actions.</summary>
public sealed record ReasonRequest(string? Reason);

/// <summary>An organization in the admin review queue.</summary>
public sealed record OrganizationReviewItemDto(
    Guid Id,
    string Name,
    BusinessType Type,
    string Address,
    double Latitude,
    double Longitude,
    string TimeZone,
    OrganizationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    string? RejectionReason,
    string? Phone,
    string OwnerName,
    string OwnerEmail);

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role,
    int ReliabilityScore,
    bool IsSuspended,
    string? SuspensionReason,
    DateTimeOffset CreatedAt);

public sealed record AuditLogDto(
    Guid Id,
    Guid? ActorId,
    string Action,
    string EntityType,
    Guid EntityId,
    string? Details,
    DateTimeOffset OccurredAt);

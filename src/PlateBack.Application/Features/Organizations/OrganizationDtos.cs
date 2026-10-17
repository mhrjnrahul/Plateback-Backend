using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Organizations;

/// <summary>Body of POST /organizations and PATCH /organizations/{id}.</summary>
public sealed record OrganizationRequest(
    string Name,
    string Type,
    string Address,
    double? Latitude,
    double? Longitude,
    string? Phone);

public sealed record OrganizationDto(
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
    string? RejectionReason)
{
    public static OrganizationDto From(Organization o) =>
        new(o.Id, o.Name, o.Type, o.Address, o.Latitude, o.Longitude, o.TimeZone, o.Status,
            o.CreatedAt, o.VerifiedAt, o.RejectionReason);
}

public sealed record MemberDto(Guid UserId, string DisplayName, string Email, MemberRole Role, DateTimeOffset JoinedAt);

public sealed record AddMemberRequest(string Email);

public sealed record DayCount(string Date, int Count);

/// <summary>GET /organizations/{id}/stats</summary>
public sealed record OrganizationStatsDto(
    int ActiveListings,
    int PortionsReserved,
    int CollectedToday,
    int MealsRescuedThisMonth,
    IReadOnlyList<DayCount> MealsRescuedByDay);

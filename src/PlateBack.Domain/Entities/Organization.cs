using PlateBack.Domain.Common;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Entities;

/// <summary>
/// A business that publishes listings. Starts Pending; an admin verifies it
/// before it can publish (flow C).
/// </summary>
public sealed class Organization : Entity
{
    public const string DefaultTimeZone = "Asia/Kathmandu";

    private readonly List<OrganizationMember> _members = [];

    private Organization() { }

    public string Name { get; private set; } = "";
    public BusinessType Type { get; private set; }
    public string Address { get; private set; } = "";
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    /// <summary>IANA time zone, used for "today" on the dashboard.</summary>
    public string TimeZone { get; private set; } = DefaultTimeZone;

    /// <summary>Only admins see it.</summary>
    public string? Phone { get; private set; }

    public OrganizationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Soft reference to the admin who verified it.</summary>
    public Guid? VerifiedBy { get; private set; }

    /// <summary>Why it was rejected or suspended, shown to the owner.</summary>
    public string? RejectionReason { get; private set; }

    public IReadOnlyCollection<OrganizationMember> Members => _members;

    public bool CanPublish => Status == OrganizationStatus.Verified;

    public static Organization Create(
        Guid id,
        string name,
        BusinessType type,
        string address,
        double latitude,
        double longitude,
        string? phone,
        Guid ownerId,
        DateTimeOffset now)
    {
        var organization = new Organization
        {
            Id = id,
            Status = OrganizationStatus.Pending,
            CreatedAt = now,
        };
        organization.UpdateDetails(name, type, address, latitude, longitude, phone);
        organization._members.Add(OrganizationMember.Create(id, ownerId, MemberRole.Owner, now));
        return organization;
    }

    public void UpdateDetails(string name, BusinessType type, string address, double latitude, double longitude, string? phone)
    {
        Name = name.Trim();
        Type = type;
        Address = address.Trim();
        Latitude = latitude;
        Longitude = longitude;
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public bool IsMember(Guid userId) => _members.Any(m => m.UserId == userId);

    public bool IsOwner(Guid userId) => _members.Any(m => m.UserId == userId && m.Role == MemberRole.Owner);

    /// <summary>Pending, rejected or suspended businesses can be verified (a suspension can be lifted).</summary>
    public void Verify(Guid adminId, DateTimeOffset now)
    {
        if (Status == OrganizationStatus.Verified)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This business is already verified.");
        }
        Status = OrganizationStatus.Verified;
        VerifiedAt = now;
        VerifiedBy = adminId;
        RejectionReason = null;
    }

    public void Reject(string reason)
    {
        if (Status != OrganizationStatus.Pending)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only businesses waiting for review can be rejected.");
        }
        Status = OrganizationStatus.Rejected;
        RejectionReason = RequireReason(reason);
    }

    public void Suspend(string reason)
    {
        if (Status != OrganizationStatus.Verified)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only verified businesses can be suspended.");
        }
        Status = OrganizationStatus.Suspended;
        RejectionReason = RequireReason(reason);
    }

    public OrganizationMember AddStaff(Guid userId, DateTimeOffset now)
    {
        if (IsMember(userId))
        {
            throw new DomainException(ErrorCodes.InvalidState, "This person is already a member.");
        }
        var member = OrganizationMember.Create(Id, userId, MemberRole.Staff, now);
        _members.Add(member);
        return member;
    }

    public void RemoveMember(Guid userId)
    {
        var member = _members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not a member.");
        if (member.Role == MemberRole.Owner && _members.Count(m => m.Role == MemberRole.Owner) == 1)
        {
            throw new DomainException(ErrorCodes.InvalidState, "A business must keep at least one owner.");
        }
        _members.Remove(member);
    }

    private static string RequireReason(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new DomainException(ErrorCodes.ValidationError, "A reason is required.", "reason")
            : reason.Trim();
}

/// <summary>Who belongs to which organization, and as what.</summary>
public sealed class OrganizationMember
{
    private OrganizationMember() { }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public MemberRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    internal static OrganizationMember Create(Guid organizationId, Guid userId, MemberRole role, DateTimeOffset now) =>
        new() { OrganizationId = organizationId, UserId = userId, Role = role, JoinedAt = now };
}

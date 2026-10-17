using PlateBack.Domain.Common;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Entities;

/// <summary>
/// The business side of an account: name, role, reliability and suspension.
/// Login data (email, password hash, lockout) lives in the Identity user with
/// the same id (decision 3).
/// </summary>
public sealed class User : Entity
{
    public const int MaxReliability = 100;
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 60;

    private User() { }

    public string DisplayName { get; private set; } = "";
    public UserRole Role { get; private set; }

    /// <summary>0-100. Starts at 100; below 60 limits the user to one active reservation.</summary>
    public int ReliabilityScore { get; private set; }

    public bool IsSuspended { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(Guid id, string displayName, DateTimeOffset now, UserRole role = UserRole.Customer)
    {
        var user = new User { Id = id, Role = role, ReliabilityScore = MaxReliability, CreatedAt = now };
        user.Rename(displayName);
        return user;
    }

    public void Rename(string displayName)
    {
        var name = displayName.Trim();
        if (name.Length is < DisplayNameMinLength or > DisplayNameMaxLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Enter your name ({DisplayNameMinLength}-{DisplayNameMaxLength} characters).",
                "displayName");
        }
        DisplayName = name;
    }

    /// <summary>How many reservations this user may hold at once.</summary>
    public int ActiveReservationLimit(BusinessRules rules) =>
        ReliabilityScore < rules.LowReliabilityBelow
            ? rules.MaxActiveReservationsLowReliability
            : rules.MaxActiveReservations;

    public void ApplyCollected(BusinessRules rules) => ChangeReliability(rules.CollectBonus);

    public void ApplyLateCancel(BusinessRules rules) => ChangeReliability(-rules.LateCancelPenalty);

    public void ApplyNoShow(BusinessRules rules) => ChangeReliability(-rules.NoShowPenalty);

    public void Suspend(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ValidationError, "A reason is required.", "reason");
        }
        if (Role == UserRole.Admin)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Admin accounts cannot be suspended.");
        }
        IsSuspended = true;
        SuspensionReason = reason.Trim();
    }

    public void Restore()
    {
        IsSuspended = false;
        SuspensionReason = null;
    }

    /// <summary>Only for seeding demo data with a given score.</summary>
    public void SetReliabilityForSeed(int score) => ReliabilityScore = Math.Clamp(score, 0, MaxReliability);

    private void ChangeReliability(int delta) =>
        ReliabilityScore = Math.Clamp(ReliabilityScore + delta, 0, MaxReliability);
}

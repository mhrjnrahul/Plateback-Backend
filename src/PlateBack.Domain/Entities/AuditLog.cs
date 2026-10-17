using PlateBack.Domain.Common;

namespace PlateBack.Domain.Entities;

/// <summary>
/// Append-only record of a state change (design doc 5.2). Entity type and id
/// are soft references, so the history survives if the row is deleted.
/// </summary>
public sealed class AuditLog : Entity
{
    private AuditLog() { }

    /// <summary>Null for system actions such as the expiry job.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>For example "ReservationCreated" or "OrganizationVerified".</summary>
    public string Action { get; private set; } = "";

    public string EntityType { get; private set; } = "";
    public Guid EntityId { get; private set; }
    public string? Details { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditLog Record(
        Guid? actorId, string action, string entityType, Guid entityId, string? details, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            OccurredAt = now,
        };
}

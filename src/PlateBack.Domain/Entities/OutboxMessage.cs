using PlateBack.Domain.Common;

namespace PlateBack.Domain.Entities;

/// <summary>
/// Transactional outbox (design doc 4.5). Written in the same transaction as
/// the change it describes, then delivered by a background job, so a message
/// exists if and only if the change committed.
/// </summary>
public sealed class OutboxMessage : Entity
{
    public const int MaxAttempts = 5;

    private OutboxMessage() { }

    /// <summary>What kind of message the payload holds, for example "Notification".</summary>
    public string Type { get; private set; } = "";

    /// <summary>JSON payload.</summary>
    public string Payload { get; private set; } = "";

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Null while the message is waiting to be delivered.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), Type = type, Payload = payload, OccurredAt = now };

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    /// <summary>Records a failed attempt; after <see cref="MaxAttempts"/> it is given up on.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (Attempts >= MaxAttempts)
        {
            ProcessedAt = now;
        }
    }
}

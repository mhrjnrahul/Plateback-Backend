using System.Text.Json;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Common;

/// <summary>Payload of an outbox message of type <see cref="MessageType"/>: one notification for several users.</summary>
public sealed record NotificationMessage(
    IReadOnlyList<Guid> UserIds,
    NotificationType Type,
    string Title,
    string Body,
    Dictionary<string, string> Data)
{
    public const string MessageType = "Notification";
}

/// <summary>
/// Audit and outbox rows are added to the same unit of work as the change
/// they describe, so they commit together or not at all (design doc 2.6).
/// </summary>
public static class RecordKeeping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Audit(
        this IAppDbContext db, Guid? actorId, string action, string entityType, Guid entityId,
        DateTimeOffset now, string? details = null) =>
        db.AuditLogs.Add(AuditLog.Record(actorId, action, entityType, entityId, details, now));

    /// <summary>Queues a notification; the outbox job delivers it.</summary>
    public static void Notify(
        this IAppDbContext db, IEnumerable<Guid> userIds, NotificationType type, string title, string body,
        DateTimeOffset now, Dictionary<string, string>? data = null)
    {
        var recipients = userIds.Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }
        var message = new NotificationMessage(recipients, type, title, body, data ?? []);
        db.OutboxMessages.Add(OutboxMessage.Create(
            NotificationMessage.MessageType, JsonSerializer.Serialize(message, Json), now));
    }

    public static void Notify(
        this IAppDbContext db, Guid userId, NotificationType type, string title, string body,
        DateTimeOffset now, Dictionary<string, string>? data = null) =>
        db.Notify([userId], type, title, body, now, data);

    public static NotificationMessage? ReadNotification(OutboxMessage message) =>
        JsonSerializer.Deserialize<NotificationMessage>(message.Payload, Json);
}

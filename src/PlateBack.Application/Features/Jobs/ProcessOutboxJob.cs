using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Jobs;

/// <summary>
/// Runs every 10 seconds: turns pending outbox messages into in-app
/// notifications and tells the recipients' browsers over SignalR. A message
/// that fails is retried up to 5 times, then kept with its last error.
/// </summary>
public sealed class ProcessOutboxJob(IAppDbContext db, ILiveUpdates live, TimeProvider time)
{
    private const int BatchSize = 100;

    /// <returns>How many messages were delivered.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var messages = await db.ClaimOutboxBatchAsync(BatchSize, cancellationToken);
        var delivered = 0;
        HashSet<Guid> recipients = [];
        foreach (var message in messages)
        {
            try
            {
                recipients.UnionWith(Deliver(message));
                message.MarkProcessed(now);
                delivered++;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                message.MarkFailed(ex.Message, now);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await live.NotificationsChangedAsync(recipients);
        return delivered;
    }

    /// <returns>The users who got a notification.</returns>
    private IReadOnlyCollection<Guid> Deliver(OutboxMessage message)
    {
        if (message.Type != NotificationMessage.MessageType)
        {
            throw new InvalidOperationException($"Unknown outbox message type {message.Type}.");
        }
        var payload = RecordKeeping.ReadNotification(message)
            ?? throw new InvalidOperationException("Empty notification payload.");
        foreach (var userId in payload.UserIds)
        {
            db.Notifications.Add(Notification.Create(userId, payload.Type, payload.Title, payload.Body, payload.Data, message.OccurredAt));
        }
        return payload.UserIds;
    }
}

using PlateBack.Application.Common;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Jobs;

/// <summary>
/// Runs every 10 seconds: turns pending outbox messages into in-app
/// notifications. Later weeks add SignalR pushes and email here. A message
/// that fails is retried up to 5 times, then kept with its last error.
/// </summary>
public sealed class ProcessOutboxJob(IAppDbContext db, TimeProvider time)
{
    private const int BatchSize = 100;

    /// <returns>How many messages were delivered.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var messages = await db.ClaimOutboxBatchAsync(BatchSize, cancellationToken);
        var delivered = 0;
        foreach (var message in messages)
        {
            try
            {
                Deliver(message);
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
        return delivered;
    }

    private void Deliver(OutboxMessage message)
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
    }
}

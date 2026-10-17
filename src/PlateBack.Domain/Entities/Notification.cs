using PlateBack.Domain.Common;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Entities;

/// <summary>An in-app message. <see cref="Data"/> holds deep-link targets such as a reservation id.</summary>
public sealed class Notification : Entity
{
    private Notification() { }

    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Dictionary<string, string> Data { get; private set; } = [];

    public static Notification Create(
        Guid userId, NotificationType type, string title, string body,
        IReadOnlyDictionary<string, string> data, DateTimeOffset now, bool isRead = false) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            Data = new Dictionary<string, string>(data),
            CreatedAt = now,
            IsRead = isRead,
        };

    public void MarkRead() => IsRead = true;
}

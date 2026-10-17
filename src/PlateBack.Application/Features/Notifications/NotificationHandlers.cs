using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Application.Common.Paging;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    bool IsRead,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Data);

/// <summary>A page of notifications plus the unread count for the bell.</summary>
public sealed record NotificationPage(IReadOnlyList<NotificationDto> Items, int Page, int PageSize, int Total, int UnreadCount);

/// <summary>GET /notifications: newest first.</summary>
public sealed class GetNotificationsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<NotificationPage> HandleAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var result = await mine
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.IsRead, n.CreatedAt, n.Data))
            .ToPageAsync(PageRequest.From(page, pageSize), cancellationToken);
        var unread = await mine.CountAsync(n => !n.IsRead, cancellationToken);
        return new NotificationPage(result.Items, result.Page, result.PageSize, result.Total, unread);
    }
}

/// <summary>POST /notifications/{id}/read and /notifications/read-all</summary>
public sealed class MarkNotificationsReadHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task MarkOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Notification");
        notification.MarkRead();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead();
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

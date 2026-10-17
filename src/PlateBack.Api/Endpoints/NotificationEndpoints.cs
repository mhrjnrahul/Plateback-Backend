using PlateBack.Application.Features.Notifications;

namespace PlateBack.Api.Endpoints;

internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();

        notifications.MapGet("/", async (int? page, int? pageSize, GetNotificationsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(page, pageSize, ct)))
            .WithSummary("My notifications")
            .WithDescription("Newest first, with unreadCount for the bell.");

        notifications.MapPost("/{id:guid}/read", async (Guid id, MarkNotificationsReadHandler handler, CancellationToken ct) =>
            {
                await handler.MarkOneAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Mark one as read")
            .ProducesProblem(StatusCodes.Status404NotFound);

        notifications.MapPost("/read-all", async (MarkNotificationsReadHandler handler, CancellationToken ct) =>
            {
                await handler.MarkAllAsync(ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Mark all as read");
    }
}

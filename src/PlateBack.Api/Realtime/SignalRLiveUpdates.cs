using Microsoft.AspNetCore.SignalR;

using PlateBack.Application.Common.Abstractions;

namespace PlateBack.Api.Realtime;

/// <summary>Sends <see cref="ILiveUpdates"/> messages through <see cref="LiveHub"/>.</summary>
internal sealed partial class SignalRLiveUpdates(IHubContext<LiveHub> hub, ILogger<SignalRLiveUpdates> logger) : ILiveUpdates
{
    public Task ListingsChangedAsync(IReadOnlyCollection<Guid> listingIds) =>
        listingIds.Count == 0 ? Task.CompletedTask
            : SendAsync(hub.Clients.All, "ListingsChanged", new { listingIds });

    public Task OrganizationChangedAsync(Guid organizationId) =>
        SendAsync(hub.Clients.Group(LiveHub.OrganizationGroup(organizationId)), "OrganizationChanged", new { organizationId });

    public Task NotificationsChangedAsync(IReadOnlyCollection<Guid> userIds) =>
        userIds.Count == 0 ? Task.CompletedTask
            : SendAsync(hub.Clients.Users([.. userIds.Select(id => id.ToString())]), "NotificationsChanged", new { });

    /// <summary>The change is already saved; polling in the browser covers a lost push.</summary>
    private async Task SendAsync(IClientProxy clients, string method, object message)
    {
        try
        {
            await clients.SendAsync(method, message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPushFailed(logger, exception, method);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live update {Method} could not be sent")]
    private static partial void LogPushFailed(ILogger logger, Exception exception, string method);
}

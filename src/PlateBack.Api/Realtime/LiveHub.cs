using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using PlateBack.Application.Common.Abstractions;

namespace PlateBack.Api.Realtime;

/// <summary>
/// /hubs/live pushes "something changed" messages to browsers (decision 9).
/// Anyone may connect, so visitors see stock change. Staff call
/// WatchOrganization to join their business's group; membership is checked in
/// the database, like every other business permission.
/// </summary>
public sealed class LiveHub(IAppDbContext db) : Hub
{
    public const string Path = "/hubs/live";

    public static string OrganizationGroup(Guid organizationId) => $"org:{organizationId}";

    public async Task WatchOrganization(Guid organizationId)
    {
        var isMember = Guid.TryParse(Context.UserIdentifier, out var userId)
            && await db.OrganizationMembers.AnyAsync(
                m => m.OrganizationId == organizationId && m.UserId == userId, Context.ConnectionAborted);
        if (!isMember)
        {
            throw new HubException("You are not a member of this business.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, OrganizationGroup(organizationId), Context.ConnectionAborted);
    }

    public Task UnwatchOrganization(Guid organizationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OrganizationGroup(organizationId), Context.ConnectionAborted);
}

/// <summary>SignalR addresses users by the access token's "sub" claim (the user id).</summary>
internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst("sub")?.Value;
}

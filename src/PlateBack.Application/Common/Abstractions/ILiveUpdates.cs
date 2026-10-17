namespace PlateBack.Application.Common.Abstractions;

/// <summary>
/// Tells connected browsers that something changed, so they refetch it
/// (SignalR, decision 9). Messages carry ids only: the data itself still comes
/// from the API with its usual permission checks. Called after the change is
/// committed, and a failed push never fails the request.
/// </summary>
public interface ILiveUpdates
{
    /// <summary>Stock, status or details of these listings changed. Sent to everyone.</summary>
    Task ListingsChangedAsync(IReadOnlyCollection<Guid> listingIds);

    /// <summary>A business's queue, listings or numbers changed. Sent to its members only.</summary>
    Task OrganizationChangedAsync(Guid organizationId);

    /// <summary>These users have new notifications. Sent to them only.</summary>
    Task NotificationsChangedAsync(IReadOnlyCollection<Guid> userIds);
}

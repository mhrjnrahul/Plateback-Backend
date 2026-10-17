using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using PlateBack.Domain.Entities;

namespace PlateBack.Application.Common.Abstractions;

/// <summary>
/// The database as the use cases see it. Implemented by the EF Core context in
/// Infrastructure. The few operations that need hand-written SQL (atomic stock
/// changes, row locks) are methods here, so the SQL stays in one place.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationMember> OrganizationMembers { get; }
    DbSet<Listing> Listings { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The concurrency guard from design doc 5.5: takes <paramref name="quantity"/>
    /// units only if the listing is Active, its window is open and enough stock
    /// is left, in one UPDATE. Returns false when another request got there first.
    /// </summary>
    Task<bool> TryTakeStockAsync(Guid listingId, int quantity, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Puts units back while the listing is still open (a cancelled reservation).</summary>
    Task ReturnStockAsync(Guid listingId, int quantity, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the user's row until the transaction ends (SELECT ... FOR UPDATE),
    /// so two requests from the same person cannot both pass the limit checks.
    /// </summary>
    Task LockUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Pending outbox messages, locked with SKIP LOCKED so parallel workers never share one.</summary>
    Task<List<OutboxMessage>> ClaimOutboxBatchAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Reserved reservations whose window has ended, locked with SKIP LOCKED.</summary>
    Task<List<Reservation>> ClaimDueNoShowsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);

    /// <summary>Live listings whose window has ended, locked with SKIP LOCKED.</summary>
    Task<List<Listing>> ClaimDueListingsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
}

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Domain.Entities;
using PlateBack.Infrastructure.Identity;

namespace PlateBack.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context: Identity's user tables plus the domain tables
/// (design doc 5.2). Table and column names are snake_case.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options), IAppDbContext
{
    public new DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Identity's users, under a name that does not clash with the domain users.</summary>
    public DbSet<ApplicationUser> IdentityUsers => base.Users;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().ToTable("identity_users");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("identity_user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("identity_user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("identity_user_tokens");
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored by name ("SoldOut"), readable in psql and safe to reorder.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new UniqueConstraintException(pg.ConstraintName, ex);
        }
    }

    // The statements below are the hand-written SQL from design doc 5.5. All
    // values are parameters (the interpolated strings become $1, $2...).

    public async Task<bool> TryTakeStockAsync(Guid listingId, int quantity, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await Database.ExecuteSqlAsync($"""
            UPDATE listings
            SET    quantity_available = quantity_available - {quantity},
                   status = CASE WHEN quantity_available - {quantity} = 0 THEN 'SoldOut' ELSE status END
            WHERE  id = {listingId}
              AND  status = 'Active'
              AND  quantity_available >= {quantity}
              AND  pickup_end > {now}
            """, cancellationToken);
        return rows == 1;
    }

    public Task ReturnStockAsync(Guid listingId, int quantity, DateTimeOffset now, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"""
            UPDATE listings
            SET    quantity_available = quantity_available + {quantity},
                   status = 'Active'
            WHERE  id = {listingId}
              AND  status IN ('Active', 'SoldOut')
              AND  pickup_end > {now}
              AND  quantity_available + {quantity} <= quantity_total
            """, cancellationToken);

    public Task LockUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {userId} FOR UPDATE", cancellationToken);

    public Task<List<OutboxMessage>> ClaimOutboxBatchAsync(int batchSize, CancellationToken cancellationToken) =>
        OutboxMessages.FromSql($"""
            SELECT * FROM outbox_messages
            WHERE  processed_at IS NULL
            ORDER  BY occurred_at
            LIMIT  {batchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

    // xmin is a system column, so SELECT * leaves it out; the row version needs it.
    public Task<List<Reservation>> ClaimDueNoShowsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        Reservations.FromSql($"""
            SELECT *, xmin FROM reservations
            WHERE  status = 'Reserved' AND expires_at <= {now}
            ORDER  BY expires_at
            LIMIT  {batchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

    public Task<List<Listing>> ClaimDueListingsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        Listings.FromSql($"""
            SELECT *, xmin FROM listings
            WHERE  status IN ('Active', 'SoldOut') AND pickup_end <= {now}
            ORDER  BY pickup_end
            LIMIT  {batchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
}

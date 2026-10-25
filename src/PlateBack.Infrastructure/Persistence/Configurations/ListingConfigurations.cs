using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PlateBack.Domain.Entities;

namespace PlateBack.Infrastructure.Persistence.Configurations;

/// <summary>
/// The rules that must never be broken are enforced by PostgreSQL itself
/// (design doc 5.4), so a bug in application code cannot oversell stock.
/// </summary>
internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("listings", t =>
        {
            t.HasCheckConstraint("ck_listings_stock", "quantity_available >= 0 AND quantity_available <= quantity_total");
            t.HasCheckConstraint("ck_listings_quantity", "quantity_total BETWEEN 1 AND 200");
            t.HasCheckConstraint("ck_listings_price", "price >= 0 AND price <= original_price");
            t.HasCheckConstraint("ck_listings_window", "pickup_end > pickup_start");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Title).HasMaxLength(Listing.TitleMaxLength).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.AllergenNotes).HasMaxLength(300).IsRequired();
        builder.Property(l => l.Address).HasMaxLength(200).IsRequired();
        builder.Property(l => l.ImageUrl).HasMaxLength(500);
        builder.Property(l => l.RemovalReason).HasMaxLength(500);

        // Optimistic concurrency on PostgreSQL's xmin system column: an edit
        // based on a stale copy fails instead of overwriting newer stock.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(l => new { l.Status, l.PickupEnd });
        builder.HasIndex(l => new { l.OrganizationId, l.Status });
        // Search (Week 5): covers every column the listing search filters, sorts
        // and counts on, for live listings only, so the count and the page are
        // index-only scans. It replaced a (latitude, longitude) index on Active
        // rows that the planner never used (0 scans under load).
        builder.HasIndex(l => l.PickupEnd)
            .HasDatabaseName("ix_listings_live")
            .HasFilter("status IN ('Active', 'SoldOut')")
            .IncludeProperties(l => new { l.Status, l.Latitude, l.Longitude, l.OrganizationId, l.Id, l.Price, l.Category, l.Title });
        builder.HasOne<Organization>().WithMany().HasForeignKey(l => l.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations", t =>
            t.HasCheckConstraint("ck_reservations_quantity", "quantity >= 1"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.PickupCode).HasMaxLength(9).IsRequired();
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(r => r.PickupCode).IsUnique();
        // One active reservation per user per listing.
        builder.HasIndex(r => new { r.ListingId, r.UserId }).IsUnique().HasFilter("status = 'Reserved'")
            .HasDatabaseName("ux_reservations_one_active_per_listing");
        builder.HasIndex(r => new { r.UserId, r.Status });
        builder.HasIndex(r => new { r.ListingId, r.Status });
        builder.HasOne<Listing>().WithMany().HasForeignKey(r => r.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

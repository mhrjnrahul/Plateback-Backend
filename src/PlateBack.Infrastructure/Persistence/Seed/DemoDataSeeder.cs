using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;
using PlateBack.Infrastructure.Identity;

namespace PlateBack.Infrastructure.Persistence.Seed;

/// <summary>
/// Demo data matching the frontend's mock API (businesses around Kathmandu,
/// Lalitpur and Bhaktapur), so Week 4 integration looks the same. Pickup
/// windows are relative to now, so the data never goes stale. Every account
/// uses the password "password123".
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, IOptions<BusinessRules> rules, TimeProvider time)
{
    public const string Password = "password123";

    /// <summary>Every application table; a fixed statement, nothing from input.</summary>
    private const string TruncateAll = """
        TRUNCATE notifications, audit_logs, outbox_messages, reservations, listings, organization_members,
                 organizations, refresh_tokens, users, identity_user_tokens, identity_user_logins,
                 identity_user_claims, identity_users CASCADE
        """;

    /// <summary>Deletes all data and inserts the demo data again.</summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync(TruncateAll, cancellationToken);
        db.ChangeTracker.Clear();
        await SeedAsync(cancellationToken);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        DateTimeOffset At(TimeSpan offset) => now + offset;
        var hours = (double h) => TimeSpan.FromHours(h);

        // ---------- Users ----------
        // One password hash for every demo account keeps seeding fast; Identity
        // still checks it the normal way at login.
        var hasher = new PasswordHasher<ApplicationUser>();
        var sharedHash = hasher.HashPassword(new ApplicationUser(), Password);
        var users = new Dictionary<string, User>();
        void AddUser(string key, string name, string email, int reliability, UserRole role = UserRole.Customer)
        {
            var id = SeedIds.For(key);
            db.IdentityUsers.Add(new ApplicationUser
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                PasswordHash = sharedHash,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                LockoutEnabled = true,
            });
            var user = User.Create(id, name, now - TimeSpan.FromDays(90), role);
            user.SetReliabilityForSeed(reliability);
            db.Users.Add(user);
            users[key] = user;
        }

        AddUser("user-maya", "Maya Rai", "maya@example.com", 90);
        AddUser("user-arjun", "Arjun Shrestha", "arjun@sunrisebakery.example", 100);
        AddUser("user-bikash", "Bikash Karki", "bikash@example.com", 100);
        AddUser("user-sita", "Sita Tamang", "sita@example.com", 96);
        AddUser("user-binod", "Binod Khadka", "binod@example.com", 72);
        AddUser("user-ramesh", "Ramesh Shrestha", "r.shrestha@mail.example", 100);
        AddUser("user-anita", "Anita Gurung", "anita@valleygreens.example", 100);
        AddUser("user-suman", "Suman Thapa", "suman@durbarsweets.example", 100);
        AddUser("user-admin", "Platform Admin", "admin@plateback.example", 100, UserRole.Admin);
        // Owners of the other demo businesses
        AddUser("user-nisha", "Nisha Joshi", "nisha@greenleaf.example", 100);
        AddUser("user-prakash", "Prakash Maharjan", "prakash@cornerbakehouse.example", 100);
        AddUser("user-kiran", "Kiran Adhikari", "kiran@freshfields.example", 100);
        AddUser("user-tenzin", "Tenzin Lama", "tenzin@momohouse.example", 100);
        AddUser("user-rita", "Rita Shakya", "rita@chiyacorner.example", 100);
        AddUser("user-deepak", "Deepak Rana", "deepak@quickbite.example", 100);
        AddUser("user-hari", "Hari Bista", "hari@oldmill.example", 100);

        // ---------- Organizations ----------
        var adminId = users["user-admin"].Id;
        var organizations = new Dictionary<string, Organization>();
        void AddOrganization(
            string key, string name, string address, double lat, double lng, BusinessType type,
            string ownerKey, TimeSpan age, OrganizationStatus status, string? reason = null)
        {
            var createdAt = now - age;
            var organization = Organization.Create(
                SeedIds.For(key), name, type, address, lat, lng, "+977 98XXXXXXXX", users[ownerKey].Id, createdAt);
            if (status is OrganizationStatus.Verified or OrganizationStatus.Suspended)
            {
                organization.Verify(adminId, createdAt);
            }
            if (status == OrganizationStatus.Suspended)
            {
                organization.Suspend(reason!);
            }
            if (status == OrganizationStatus.Rejected)
            {
                organization.Reject(reason!);
            }
            db.Organizations.Add(organization);
            organizations[key] = organization;
        }

        AddOrganization("org-sunrise", "Sunrise Bakery", "Thamel Rd 12, Kathmandu", 27.7154, 85.3123,
            BusinessType.Bakery, "user-arjun", TimeSpan.FromDays(60), OrganizationStatus.Verified);
        AddOrganization("org-greenleaf", "Green Leaf Cafe", "Jhamsikhel, Lalitpur", 27.6795, 85.3088,
            BusinessType.Cafe, "user-nisha", TimeSpan.FromDays(45), OrganizationStatus.Verified);
        AddOrganization("org-corner", "Corner Bakehouse", "Durbar Square, Bhaktapur", 27.6722, 85.428,
            BusinessType.Bakery, "user-prakash", TimeSpan.FromDays(40), OrganizationStatus.Verified);
        AddOrganization("org-freshfields", "Fresh Fields", "New Baneshwor, Kathmandu", 27.689, 85.342,
            BusinessType.Grocer, "user-kiran", TimeSpan.FromDays(30), OrganizationStatus.Verified);
        AddOrganization("org-momo", "Himalayan Momo House", "Boudha, Kathmandu", 27.7215, 85.362,
            BusinessType.Restaurant, "user-tenzin", TimeSpan.FromDays(25), OrganizationStatus.Verified);
        AddOrganization("org-chiya", "Chiya Corner", "Mangal Bazaar, Lalitpur", 27.673, 85.325,
            BusinessType.Cafe, "user-rita", TimeSpan.FromDays(20), OrganizationStatus.Verified);
        // The admin's review queue
        AddOrganization("org-hilltop", "Hilltop Kitchen", "Kupondole, Lalitpur", 27.6866, 85.3165,
            BusinessType.Restaurant, "user-ramesh", hours(3), OrganizationStatus.Pending);
        AddOrganization("org-valleygreens", "Valley Greens", "Lazimpat, Kathmandu", 27.7225, 85.3205,
            BusinessType.Grocer, "user-anita", hours(5), OrganizationStatus.Pending);
        AddOrganization("org-durbar", "Durbar Sweets", "Taumadhi, Bhaktapur", 27.6714, 85.4298,
            BusinessType.Bakery, "user-suman", hours(26), OrganizationStatus.Pending);
        AddOrganization("org-quickbite", "Quick Bite Express", "Kalanki, Kathmandu", 27.6934, 85.2817,
            BusinessType.Restaurant, "user-deepak", TimeSpan.FromDays(10), OrganizationStatus.Rejected,
            "Could not confirm the business address.");
        AddOrganization("org-oldmill", "Old Mill Pastries", "Pulchowk, Lalitpur", 27.6782, 85.3167,
            BusinessType.Bakery, "user-hari", TimeSpan.FromDays(50), OrganizationStatus.Suspended,
            "Repeated complaints about food quality.");

        // ---------- Listings ----------
        var listings = new Dictionary<string, Listing>();
        void AddListing(
            string key, string orgKey, string title, ListingCategory category, string description, string allergens,
            int price, int originalPrice, int total, int available, double startInHours, double lengthHours,
            ListingStatus? status = null)
        {
            var organization = organizations[orgKey];
            var start = At(hours(startInHours));
            var createdAt = start - hours(4);
            var listing = Listing.Create(organization.Id,
                new ListingDetails(title, category, description, total, originalPrice, price, start,
                    start + hours(lengthHours), organization.Address, organization.Latitude, organization.Longitude,
                    allergens, null),
                createdAt, rules.Value, SeedIds.For(key));
            listing.SetStockForSeed(available, status ?? (available == 0 ? ListingStatus.SoldOut : ListingStatus.Active));
            db.Listings.Add(listing);
            listings[key] = listing;
        }

        AddListing("listing-pastry", "org-sunrise", "Mixed pastry box", ListingCategory.Bakery,
            "A mix of croissants, danishes and rolls baked today. Contents vary. Best eaten within 24 hours.",
            "Contains gluten, milk and eggs; may contain nuts.", 240, 800, 10, 3, 1, 2);
        AddListing("listing-croissant", "org-sunrise", "Croissant trio", ListingCategory.Bakery,
            "Three butter croissants from this morning's batch.", "Contains gluten, milk and eggs.", 150, 360, 12, 8, 0.5, 2);
        AddListing("listing-sourdough", "org-sunrise", "Sourdough loaf", ListingCategory.Bakery,
            "One large sourdough loaf, baked today.", "Contains gluten.", 220, 450, 6, 4, 1, 2);
        AddListing("listing-thali", "org-greenleaf", "Veg thali set", ListingCategory.Meals,
            "Rice, dal, two vegetable curries, pickle and papad. Packed to take away.",
            "Contains milk; may contain mustard and nuts.", 150, 400, 10, 7, -0.5, 2);
        AddListing("listing-bread", "org-corner", "Bread & rolls bag", ListingCategory.Bakery,
            "A bag of today's bread and rolls. Free; please bring your own bag.",
            "Contains gluten; may contain sesame.", 0, 300, 6, 2, 1.5, 1);
        AddListing("listing-salad", "org-freshfields", "Salad bowls x2", ListingCategory.Meals,
            "Two fresh salad bowls with seasonal vegetables and a lemon dressing.",
            "May contain sesame and mustard.", 200, 500, 8, 5, 2.5, 1);
        AddListing("listing-momo", "org-momo", "Momo platter", ListingCategory.Meals,
            "Twenty steamed vegetable momos with tomato achar.", "Contains gluten and sesame.", 180, 380, 6, 4, 2, 1);
        AddListing("listing-surprise", "org-corner", "Surprise bag", ListingCategory.Other,
            "A surprise selection of what is left at closing: bread, pastries or cakes.",
            "Contains gluten, milk and eggs; may contain nuts.", 250, 600, 5, 5, 3, 1.5);
        AddListing("listing-chiya", "org-chiya", "Milk tea and cookies", ListingCategory.Beverages,
            "A flask of milk tea (serves 2) with a box of homemade cookies.",
            "Contains milk, gluten and eggs.", 40, 80, 15, 11, 0.25, 2);
        AddListing("listing-juice", "org-greenleaf", "Fresh juice bottles", ListingCategory.Beverages,
            "Two bottles of cold-pressed orange and carrot juice.", "None known.", 100, 240, 8, 0, 1, 2);
        AddListing("listing-vegbox", "org-freshfields", "Fruit and veg box", ListingCategory.Groceries,
            "Seasonal fruit and vegetables that are a little bruised but perfectly good.",
            "None known.", 120, 350, 10, 8, 24, 1);
        // Past listings, only referenced by reservation history
        AddListing("listing-old-cinnamon", "org-sunrise", "Cinnamon rolls", ListingCategory.Bakery,
            "Four cinnamon rolls.", "Contains gluten, milk and eggs.", 140, 320, 8, 0, -50, 2, ListingStatus.Expired);
        AddListing("listing-old-dalbhat", "org-momo", "Dal bhat for two", ListingCategory.Meals,
            "Dal bhat with seasonal vegetables.", "May contain mustard.", 200, 450, 6, 1, -98, 2, ListingStatus.Expired);
        AddListing("listing-old-bagels", "org-corner", "Bagel bag", ListingCategory.Bakery,
            "Six plain bagels.", "Contains gluten; may contain sesame.", 120, 280, 6, 0, -170, 2, ListingStatus.Expired);

        // ---------- Reservations ----------
        void AddReservation(
            string key, string userKey, string listingKey, int quantity, ReservationStatus status, string code,
            DateTimeOffset? collectedAt = null, DateTimeOffset? cancelledAt = null)
        {
            var listing = listings[listingKey];
            db.Reservations.Add(Reservation.Seed(
                SeedIds.For(key), listing, users[userKey].Id, quantity, code, status, listing.PickupStart - hours(3),
                status == ReservationStatus.Collected ? collectedAt ?? listing.PickupStart : null, cancelledAt));
        }

        AddReservation("res-maya-pastry", "user-maya", "listing-pastry", 1, ReservationStatus.Reserved, "K7M2-9QXA");
        AddReservation("res-maya-vegbox", "user-maya", "listing-vegbox", 2, ReservationStatus.Reserved, "R4TW-8HNE");
        AddReservation("res-maya-cinnamon", "user-maya", "listing-old-cinnamon", 2, ReservationStatus.Collected, "C9PB-3KDA");
        AddReservation("res-maya-dalbhat", "user-maya", "listing-old-dalbhat", 2, ReservationStatus.Collected, "D2XM-7QRT");
        AddReservation("res-maya-bagels", "user-maya", "listing-old-bagels", 2, ReservationStatus.Collected, "B6HN-4WJY");
        AddReservation("res-maya-cancelled", "user-maya", "listing-old-cinnamon", 1, ReservationStatus.Cancelled, "E3VK-9PAU",
            cancelledAt: At(hours(-52)));
        AddReservation("res-bikash-pastry", "user-bikash", "listing-pastry", 2, ReservationStatus.Reserved, "M8QA-2ZCE");
        AddReservation("res-sita-croissant", "user-sita", "listing-croissant", 1, ReservationStatus.Collected, "S5JD-6TRK",
            collectedAt: At(TimeSpan.FromMinutes(-20)));
        AddReservation("res-binod-sourdough", "user-binod", "listing-sourdough", 2, ReservationStatus.Reserved, "N7WF-3GHP");

        // ---------- Notifications ----------
        void AddNotification(
            string userKey, NotificationType type, string title, string body, bool isRead, TimeSpan age,
            Dictionary<string, string> data) =>
            db.Notifications.Add(Notification.Create(users[userKey].Id, type, title, body, data, now - age, isRead));

        var mayaPastry = SeedIds.For("res-maya-pastry").ToString();
        AddNotification("user-maya", NotificationType.ReservationConfirmed, "Reservation confirmed",
            "Mixed pastry box from Sunrise Bakery. Show code K7M2-9QXA at pickup.", false, TimeSpan.FromMinutes(2),
            new() { ["reservationId"] = mayaPastry });
        AddNotification("user-maya", NotificationType.PickupWindowOpening, "Pickup window opens in 1 hour",
            "Sunrise Bakery, Thamel Rd 12.", false, TimeSpan.FromMinutes(10), new() { ["reservationId"] = mayaPastry });
        AddNotification("user-maya", NotificationType.NewListingNearby, "New listing nearby",
            "Fresh Fields posted Salad bowls x2.", false, TimeSpan.FromMinutes(40),
            new() { ["listingId"] = SeedIds.For("listing-salad").ToString() });
        AddNotification("user-maya", NotificationType.ListingCancelled, "Reservation cancelled by business",
            "Corner Bakehouse cancelled a listing you reserved.", true, TimeSpan.FromDays(1), []);
        AddNotification("user-maya", NotificationType.ReservationCollected, "Thanks for rescuing food!",
            "Collected: Bagel bag from Corner Bakehouse.", true, TimeSpan.FromDays(2),
            new() { ["reservationId"] = SeedIds.For("res-maya-bagels").ToString() });
        AddNotification("user-arjun", NotificationType.ReservationConfirmed, "New reservation",
            "Maya Rai reserved 1 x Mixed pastry box.", false, TimeSpan.FromMinutes(2),
            new() { ["reservationId"] = mayaPastry, ["organizationId"] = organizations["org-sunrise"].Id.ToString() });

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}

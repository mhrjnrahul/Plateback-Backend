using System.Net;
using System.Net.Http.Json;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

/// <summary>
/// Week 5: endpoints the coverage report showed no test reaching (0% or close).
/// Each test says in its name what would go wrong for a real user if it broke.
/// </summary>
public class ManagementTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private sealed record AdminUserBody(Guid Id, string Email, string DisplayName, bool IsSuspended, string? SuspensionReason);

    private Task<HttpResponseMessage> ReserveAsync(HttpClient client, string listing, int quantity = 1) =>
        client.PostAsJsonAsync($"/api/v1/listings/{Id(listing)}/reservations", new { quantity }, Ct);

    // ---------- The business's own listings table (dashboard) ----------

    [Fact]
    public async Task The_dashboard_lists_upcoming_listings_soonest_first_then_past_ones_newest_first()
    {
        // Sorting moved from C# into SQL in Week 5; this pins the order the dashboard relies on.
        var arjun = await ArjunAsync();
        var page = await ReadAsync<PageBody<ListingBody>>(
            await arjun.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings?pageSize=50", Ct));

        page.Total.ShouldBe(page.Items.Count);
        var now = Clock.GetUtcNow();
        var upcoming = page.Items.TakeWhile(l => l.PickupEnd > now).ToList();
        var past = page.Items.Skip(upcoming.Count).ToList();
        upcoming.ShouldNotBeEmpty();
        past.ShouldNotBeEmpty();
        past.ShouldAllBe(l => l.PickupEnd <= now);
        upcoming.Select(l => l.PickupStart).ShouldBe(upcoming.Select(l => l.PickupStart).Order());
        past.Select(l => l.PickupStart).ShouldBe(past.Select(l => l.PickupStart).OrderDescending());
    }

    [Fact]
    public async Task The_dashboard_listings_page_and_count_agree()
    {
        var arjun = await ArjunAsync();
        var all = await ReadAsync<PageBody<ListingBody>>(
            await arjun.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings?pageSize=50", Ct));
        var second = await ReadAsync<PageBody<ListingBody>>(
            await arjun.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings?page=2&pageSize=2", Ct));

        second.Total.ShouldBe(all.Total);
        second.Items.Select(l => l.Id).ShouldBe(all.Items.Skip(2).Take(2).Select(l => l.Id));
    }

    [Fact]
    public async Task Customers_cannot_read_a_business_listings_table()
    {
        var maya = await MayaAsync();
        await ProblemAsync(await maya.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings", Ct),
            HttpStatusCode.Forbidden, "NOT_ORG_MEMBER");
    }

    [Fact]
    public async Task An_unknown_status_filter_is_a_validation_error_not_a_crash()
    {
        var arjun = await ArjunAsync();
        await ProblemAsync(await arjun.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings?status=Lost", Ct),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ---------- Business details ----------

    [Fact]
    public async Task Members_see_the_businesses_they_belong_to()
    {
        var arjun = await ArjunAsync();
        var mine = await ReadAsync<List<OrganizationBody>>(await arjun.GetAsync("/api/v1/organizations/mine", Ct));
        mine.ShouldHaveSingleItem().Name.ShouldBe("Sunrise Bakery");

        var maya = await MayaAsync();
        (await ReadAsync<List<OrganizationBody>>(await maya.GetAsync("/api/v1/organizations/mine", Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Owners_can_edit_their_business_details()
    {
        var arjun = await ArjunAsync();
        var response = await arjun.PatchAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}", new
        {
            name = "Sunrise Bakery & Cafe",
            type = "Cafe",
            address = "Thamel Marg 12, Kathmandu",
            latitude = 27.7154,
            longitude = 85.3123,
            phone = "+977 1 4700000",
        }, Ct);
        var updated = await ReadAsync<OrganizationBody>(response);
        updated.Name.ShouldBe("Sunrise Bakery & Cafe");
        updated.Type.ShouldBe("Cafe");
    }

    [Fact]
    public async Task Staff_cannot_edit_business_details()
    {
        var arjun = await ArjunAsync();
        (await arjun.PostAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}/members", new { email = "sita@example.com" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var sita = await SignInAsync("sita@example.com");

        var response = await sita.PatchAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}", new
        {
            name = "Taken over",
            type = "Bakery",
            address = "Somewhere",
            latitude = 27.7,
            longitude = 85.3,
        }, Ct);
        await ProblemAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    // ---------- Admin: listings and accounts ----------

    [Fact]
    public async Task Removing_a_listing_cancels_its_reservations_and_tells_customers_and_staff()
    {
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/listings/{Id("listing-pastry")}/remove",
            new { reason = "Photo shows a different product." }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var pastry = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-pastry")}", Ct));
        pastry.Status.ShouldBe("Cancelled");
        var maya = await MayaAsync();
        var history = await ReadAsync<PageBody<ReservationBody>>(await maya.GetAsync("/api/v1/reservations/mine?status=history", Ct));
        history.Items.ShouldContain(r => r.ListingId == Id("listing-pastry") && r.Status == "Cancelled");

        await Factory.RunOutboxAsync();
        var mayaNotes = await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct));
        mayaNotes.Items.ShouldContain(n => n.Type == "ListingCancelled");
        var arjun = await ArjunAsync();
        var staffNotes = await ReadAsync<NotificationPageBody>(await arjun.GetAsync("/api/v1/notifications", Ct));
        staffNotes.Items.ShouldContain(n => n.Title == "Listing removed by PlateBack" && n.Body.Contains("different product"));
    }

    [Fact]
    public async Task Removing_a_listing_needs_a_reason()
    {
        var admin = await AdminAsync();
        await ProblemAsync(await admin.PostAsJsonAsync($"/api/v1/admin/listings/{Id("listing-pastry")}/remove", new { reason = " " }, Ct),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Admins_find_accounts_by_name_or_exact_email()
    {
        var admin = await AdminAsync();
        var byName = await ReadAsync<PageBody<AdminUserBody>>(await admin.GetAsync("/api/v1/admin/users?q=maya", Ct));
        byName.Items.ShouldHaveSingleItem().Email.ShouldBe("maya@example.com");

        var byEmail = await ReadAsync<PageBody<AdminUserBody>>(await admin.GetAsync("/api/v1/admin/users?q=bikash@example.com", Ct));
        byEmail.Items.ShouldHaveSingleItem().DisplayName.ShouldBe("Bikash Karki");
    }

    [Fact]
    public async Task Suspending_an_account_ends_its_sessions_and_blocks_reserving_with_the_token_it_still_holds()
    {
        var login = await Anonymous().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "maya@example.com", password = "password123" }, Ct);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("plateback_refresh=", StringComparison.Ordinal)).Split(';')[0];
        var maya = await MayaAsync();

        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync($"/api/v1/admin/users/{Id("user-maya")}/suspend", new { reason = "Reported for reselling." }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Her access token is still valid for up to 15 minutes, but it cannot be used to reserve...
        await ProblemAsync(await ReserveAsync(maya, "listing-thali"), HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
        // ...and the session cannot be extended.
        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refresh.Headers.Add("Cookie", cookie);
        await ProblemAsync(await Anonymous().SendAsync(refresh, Ct), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_restored_account_can_log_in_again()
    {
        var admin = await AdminAsync();
        await admin.PostAsJsonAsync($"/api/v1/admin/users/{Id("user-bikash")}/suspend", new { reason = "Mistake." }, Ct);
        (await admin.PostAsync($"/api/v1/admin/users/{Id("user-bikash")}/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var users = await ReadAsync<PageBody<AdminUserBody>>(await admin.GetAsync("/api/v1/admin/users?q=bikash", Ct));
        users.Items.ShouldHaveSingleItem().IsSuspended.ShouldBeFalse();
        (await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { email = "bikash@example.com", password = "password123" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- Notifications ----------

    [Fact]
    public async Task Mark_all_read_clears_the_unread_count()
    {
        var maya = await MayaAsync();
        (await ReserveAsync(maya, "listing-thali")).StatusCode.ShouldBe(HttpStatusCode.Created);
        await Factory.RunOutboxAsync();
        (await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct))).UnreadCount.ShouldBeGreaterThan(0);

        (await maya.PostAsync("/api/v1/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct));
        after.UnreadCount.ShouldBe(0);
        after.Items.ShouldAllBe(n => n.IsRead);
    }

    [Fact]
    public async Task Nobody_can_mark_someone_elses_notification_read()
    {
        var maya = await MayaAsync();
        (await ReserveAsync(maya, "listing-thali")).StatusCode.ShouldBe(HttpStatusCode.Created);
        await Factory.RunOutboxAsync();
        var mine = (await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct))).Items.First();

        var bikash = await SignInAsync("bikash@example.com");
        await ProblemAsync(await bikash.PostAsync($"/api/v1/notifications/{mine.Id}/read", null, Ct), HttpStatusCode.NotFound, "NOT_FOUND");
        (await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct))).Items
            .Single(n => n.Id == mine.Id).IsRead.ShouldBeFalse();
    }
}

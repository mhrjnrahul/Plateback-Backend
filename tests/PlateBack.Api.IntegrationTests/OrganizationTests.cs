using System.Net;
using System.Net.Http.Json;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

public class OrganizationTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private sealed record StatsBody(int ActiveListings, int PortionsReserved, int CollectedToday, int MealsRescuedThisMonth, List<DayBody> MealsRescuedByDay);

    private sealed record DayBody(string Date, int Count);

    private sealed record MemberBody(Guid UserId, string DisplayName, string Email, string Role);

    [Fact]
    public async Task A_new_business_starts_pending_with_the_caller_as_owner()
    {
        var maya = await MayaAsync();
        var response = await maya.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = "Maya's Momos",
            type = "Restaurant",
            address = "Lazimpat, Kathmandu",
            latitude = 27.72,
            longitude = 85.32,
        }, Ct);
        var organization = await ReadAsync<OrganizationBody>(response, HttpStatusCode.Created);
        organization.Status.ShouldBe("Pending");

        var me = await ReadAsync<UserBody>(await maya.GetAsync("/api/v1/users/me", Ct));
        me.Memberships.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            m => m.OrganizationId.ShouldBe(organization.Id), m => m.Role.ShouldBe("Owner"));
    }

    [Fact]
    public async Task An_unknown_business_type_is_rejected()
    {
        var maya = await MayaAsync();
        var response = await maya.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = "Rocket Cafe",
            type = "Spaceport",
            address = "Lazimpat, Kathmandu",
            latitude = 27.72,
            longitude = 85.32,
        }, Ct);
        (await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR")).Errors!.Keys.ShouldContain("type");
    }

    [Fact]
    public async Task Pending_businesses_are_hidden_from_strangers()
    {
        await ProblemAsync(await Anonymous().GetAsync($"/api/v1/organizations/{Id("org-hilltop")}", Ct), HttpStatusCode.NotFound, "NOT_FOUND");
        var ramesh = await SignInAsync("r.shrestha@mail.example");
        (await ramesh.GetAsync($"/api/v1/organizations/{Id("org-hilltop")}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dashboard_stats_count_real_collections_in_the_business_time_zone()
    {
        var arjun = await ArjunAsync();
        var stats = await ReadAsync<StatsBody>(await arjun.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/stats", Ct));
        stats.ShouldSatisfyAllConditions(
            s => s.ActiveListings.ShouldBe(3),
            s => s.PortionsReserved.ShouldBe(5), // Maya 1, Bikash 2, Binod 2
            s => s.CollectedToday.ShouldBe(1), // Sita, 20 minutes ago
            s => s.MealsRescuedThisMonth.ShouldBe(3), // plus Maya's cinnamon rolls on 15 October
            s => s.MealsRescuedByDay.Count.ShouldBe(14),
            s => s.MealsRescuedByDay[^1].ShouldBe(new DayBody("2026-10-17", 1)));
    }

    [Fact]
    public async Task Only_members_see_a_business_dashboard()
    {
        var maya = await MayaAsync();
        await ProblemAsync(await maya.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/stats", Ct), HttpStatusCode.Forbidden, "NOT_ORG_MEMBER");
    }

    [Fact]
    public async Task Owners_add_staff_who_can_then_hand_over_food()
    {
        var arjun = await ArjunAsync();
        var team = await ReadAsync<List<MemberBody>>(await arjun.PostAsJsonAsync(
            $"/api/v1/organizations/{Id("org-sunrise")}/members", new { email = "sita@example.com" }, Ct));
        team.ShouldContain(m => m.DisplayName == "Sita Tamang" && m.Role == "Staff");

        var sita = await SignInAsync("sita@example.com");
        var collected = await sita.PostAsJsonAsync("/api/v1/reservations/verify", new { pickupCode = "M8QA-2ZCE" }, Ct);
        collected.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave()
    {
        var arjun = await ArjunAsync();
        var response = await arjun.DeleteAsync($"/api/v1/organizations/{Id("org-sunrise")}/members/{Id("user-arjun")}", Ct);
        await ProblemAsync(response, HttpStatusCode.Conflict, "INVALID_STATE");
    }

    [Fact]
    public async Task Removed_staff_lose_access_immediately()
    {
        var arjun = await ArjunAsync();
        await arjun.PostAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}/members", new { email = "sita@example.com" }, Ct);
        var sita = await SignInAsync("sita@example.com");
        (await sita.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/stats", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await arjun.DeleteAsync($"/api/v1/organizations/{Id("org-sunrise")}/members/{Id("user-sita")}", Ct);
        // Same token as before: membership is checked in the database, not the token.
        await ProblemAsync(await sita.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/stats", Ct), HttpStatusCode.Forbidden, "NOT_ORG_MEMBER");
    }
}

public class AdminTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Customers_cannot_use_admin_endpoints()
    {
        var maya = await MayaAsync();
        await ProblemAsync(await maya.GetAsync("/api/v1/admin/organizations", Ct), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task The_review_queue_is_oldest_first_with_owner_details()
    {
        var admin = await AdminAsync();
        var queue = await ReadAsync<PageBody<OrganizationBody>>(await admin.GetAsync("/api/v1/admin/organizations?status=Pending", Ct));
        queue.Items.Select(o => o.Name).ShouldBe(["Durbar Sweets", "Valley Greens", "Hilltop Kitchen"]);
        queue.Items[2].ShouldSatisfyAllConditions(
            o => o.OwnerName.ShouldBe("Ramesh Shrestha"),
            o => o.OwnerEmail.ShouldBe("r.shrestha@mail.example"));
    }

    [Fact]
    public async Task Verifying_lets_the_business_publish_and_tells_the_owner()
    {
        var admin = await AdminAsync();
        var verified = await ReadAsync<OrganizationBody>(await admin.PostAsync($"/api/v1/admin/organizations/{Id("org-hilltop")}/verify", null, Ct));
        verified.Status.ShouldBe("Verified");

        await Factory.RunOutboxAsync();
        var ramesh = await SignInAsync("r.shrestha@mail.example");
        var notifications = await ReadAsync<NotificationPageBody>(await ramesh.GetAsync("/api/v1/notifications", Ct));
        notifications.Items.ShouldContain(n => n.Type == "OrganizationVerified" && n.Title == "Your business is verified");
    }

    [Fact]
    public async Task Rejecting_needs_a_reason()
    {
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/organizations/{Id("org-hilltop")}/reject", new { reason = "" }, Ct);
        (await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR")).Errors!.Keys.ShouldContain("reason");
    }

    [Fact]
    public async Task Suspending_a_business_takes_its_listings_down()
    {
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/organizations/{Id("org-sunrise")}/suspend",
            new { reason = "Food safety complaint under review." }, Ct);
        (await ReadAsync<OrganizationBody>(response)).Status.ShouldBe("Suspended");

        var search = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync("/api/v1/listings?q=sunrise", Ct));
        search.Total.ShouldBe(0);
        var pastry = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-pastry")}", Ct));
        pastry.Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task A_suspended_account_cannot_log_in()
    {
        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync($"/api/v1/admin/users/{Id("user-bikash")}/suspend", new { reason = "Repeated no-shows." }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { email = "bikash@example.com", password = "password123" }, Ct);
        await ProblemAsync(response, HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Every_decision_lands_in_the_audit_log()
    {
        var admin = await AdminAsync();
        await admin.PostAsync($"/api/v1/admin/organizations/{Id("org-hilltop")}/verify", null, Ct);
        var log = await ReadAsync<PageBody<Dictionary<string, object>>>(
            await admin.GetAsync($"/api/v1/admin/audit-logs?entityId={Id("org-hilltop")}", Ct));
        log.Items.ShouldHaveSingleItem()["action"].ToString().ShouldBe("OrganizationVerified");
    }
}

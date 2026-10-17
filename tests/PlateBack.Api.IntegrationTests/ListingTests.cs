using System.Net;
using System.Net.Http.Json;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

public class ListingTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private const string Thamel = "lat=27.7154&lng=85.3123";

    private object NewListing(int minutes = 120, int price = 120, int originalPrice = 300, int quantity = 5) => new
    {
        title = "Banana bread",
        category = "Bakery",
        description = "Two loaves.",
        quantityTotal = quantity,
        originalPrice,
        price,
        pickupStart = Clock.Now.AddHours(1),
        pickupEnd = Clock.Now.AddHours(1).AddMinutes(minutes),
        address = "Thamel Rd 12, Kathmandu",
        allergenNotes = "Contains gluten and eggs.",
    };

    [Fact]
    public async Task Search_returns_live_listings_from_verified_businesses_with_sold_out_last()
    {
        var page = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync("/api/v1/listings", Ct));
        page.Total.ShouldBe(11);
        page.Items.ShouldAllBe(l => l.Organization.IsVerified);
        page.Items.Last().Title.ShouldBe("Fresh juice bottles");
        page.Items.Last().Status.ShouldBe("SoldOut");
        page.Items.ShouldNotContain(l => l.Title == "Cinnamon rolls"); // expired
    }

    [Fact]
    public async Task Search_by_location_keeps_to_the_radius_and_sorts_by_distance()
    {
        var page = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync($"/api/v1/listings?{Thamel}", Ct));
        page.Total.ShouldBe(9);
        page.Items.ShouldNotContain(l => l.Organization.Name == "Corner Bakehouse"); // Bhaktapur, about 11 km away
        page.Items[0].DistanceKm.ShouldBe(0);
        var available = page.Items.Where(l => l.Status == "Active").Select(l => l.DistanceKm!.Value).ToList();
        available.ShouldBe(available.OrderBy(d => d).ToList());
    }

    [Fact]
    public async Task A_wider_radius_finds_more()
    {
        var page = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync($"/api/v1/listings?{Thamel}&radiusKm=25", Ct));
        page.Total.ShouldBe(11);
    }

    [Theory]
    [InlineData("category=Beverages", 2)]
    [InlineData("maxPrice=0", 1)]
    [InlineData("q=momo", 1)]
    [InlineData("q=beverage", 2)] // matches the category name
    [InlineData("q=sunrise", 3)]
    [InlineData("q=corner", 3)] // Corner Bakehouse (2) and Chiya Corner (1)
    [InlineData("q=bakehouse&category=Other", 1)]
    public async Task Search_filters(string query, int expected)
    {
        var page = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync($"/api/v1/listings?{query}", Ct));
        page.Total.ShouldBe(expected);
    }

    [Fact]
    public async Task Search_pages_the_results()
    {
        var page = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync("/api/v1/listings?page=2&pageSize=4", Ct));
        page.ShouldSatisfyAllConditions(p => p.Items.Count.ShouldBe(4), p => p.Page.ShouldBe(2), p => p.Total.ShouldBe(11));
    }

    [Fact]
    public async Task Bad_search_parameters_are_400()
    {
        var problem = await ProblemAsync(await Anonymous().GetAsync("/api/v1/listings?sort=random&lat=27.7", Ct),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!.Keys.ShouldContain("sort");
        problem.Errors.Keys.ShouldContain("lat");
    }

    [Fact]
    public async Task Listing_detail_has_stock_and_the_per_person_limit()
    {
        var listing = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-pastry")}", Ct));
        listing.ShouldSatisfyAllConditions(
            l => l.Title.ShouldBe("Mixed pastry box"),
            l => l.QuantityAvailable.ShouldBe(3),
            l => l.MaxPerUser.ShouldBe(2),
            l => l.Price.ShouldBe(240));
    }

    [Fact]
    public async Task An_unknown_listing_is_404()
    {
        await ProblemAsync(await Anonymous().GetAsync($"/api/v1/listings/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task A_verified_business_can_publish_and_it_appears_in_search()
    {
        var arjun = await ArjunAsync();
        var response = await arjun.PostAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings", NewListing(), Ct);
        var created = await ReadAsync<ListingBody>(response, HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/listings/{created.Id}");

        var search = await ReadAsync<PageBody<ListingBody>>(await Anonymous().GetAsync("/api/v1/listings?q=banana", Ct));
        search.Items.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task A_pending_business_cannot_publish()
    {
        var ramesh = await SignInAsync("r.shrestha@mail.example");
        var response = await ramesh.PostAsJsonAsync($"/api/v1/organizations/{Id("org-hilltop")}/listings", NewListing(), Ct);
        await ProblemAsync(response, HttpStatusCode.Forbidden, "ORG_NOT_VERIFIED");
    }

    [Fact]
    public async Task Only_members_can_publish_for_a_business()
    {
        var maya = await MayaAsync();
        var response = await maya.PostAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings", NewListing(), Ct);
        await ProblemAsync(response, HttpStatusCode.Forbidden, "NOT_ORG_MEMBER");
    }

    [Fact]
    public async Task A_ten_minute_window_is_rejected_on_the_end_field()
    {
        var arjun = await ArjunAsync();
        var response = await arjun.PostAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings", NewListing(minutes: 10), Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!["pickupEnd"].ShouldHaveSingleItem().ShouldContain("15 minutes");
    }

    [Fact]
    public async Task Price_above_MRP_is_rejected()
    {
        var arjun = await ArjunAsync();
        var response = await arjun.PostAsJsonAsync($"/api/v1/organizations/{Id("org-sunrise")}/listings", NewListing(price: 400), Ct);
        (await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR")).Errors!.Keys.ShouldContain("price");
    }

    [Fact]
    public async Task Editing_cannot_drop_quantity_below_what_is_reserved()
    {
        // The pastry box has 10 units, 7 of them reserved.
        var arjun = await ArjunAsync();
        var response = await arjun.PutAsJsonAsync($"/api/v1/listings/{Id("listing-pastry")}", NewListing(quantity: 6), Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!["quantityTotal"].ShouldHaveSingleItem().ShouldContain("7 already reserved");
    }

    [Fact]
    public async Task Cancelling_a_listing_cancels_its_reservations_and_tells_the_customers()
    {
        var arjun = await ArjunAsync();
        var cancelled = await ReadAsync<ListingBody>(await arjun.PostAsync($"/api/v1/listings/{Id("listing-pastry")}/cancel", null, Ct));
        cancelled.Status.ShouldBe("Cancelled");

        await Factory.RunOutboxAsync();
        var maya = await MayaAsync();
        var notifications = await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct));
        notifications.Items[0].ShouldSatisfyAllConditions(
            n => n.Type.ShouldBe("ListingCancelled"),
            n => n.Body.ShouldContain("Mixed pastry box"));
        var history = await ReadAsync<PageBody<ReservationBody>>(await maya.GetAsync("/api/v1/reservations/mine?status=history", Ct));
        history.Items.ShouldContain(r => r.Id == Id("res-maya-pastry") && r.Status == "Cancelled" && !r.LateCancel);
    }

    [Fact]
    public async Task Impact_numbers_count_only_real_data()
    {
        var impact = await ReadAsync<Dictionary<string, int>>(await Anonymous().GetAsync("/api/v1/stats/impact", Ct));
        impact["mealsRescued"].ShouldBe(7); // collected portions in the demo data
        impact["partnerBusinesses"].ShouldBe(6);
        impact["activeListings"].ShouldBe(10);
    }
}

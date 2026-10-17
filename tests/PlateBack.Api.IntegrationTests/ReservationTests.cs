using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

public class ReservationTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private static Task<HttpResponseMessage> ReserveAsync(HttpClient client, Guid listingId, int quantity = 1) =>
        client.PostAsJsonAsync($"/api/v1/listings/{listingId}/reservations", new { quantity }, Ct);

    private async Task<HttpClient> NewCustomerAsync(string name)
    {
        var client = Anonymous();
        var email = $"{name.ToLowerInvariant()}@example.com";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { displayName = name, email, password = "password123" }, Ct);
        var auth = await ReadAsync<AuthBody>(response, HttpStatusCode.Created);
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        return client;
    }

    [Fact]
    public async Task Reserving_returns_a_pickup_code_and_takes_the_stock()
    {
        var maya = await MayaAsync();
        var response = await ReserveAsync(maya, Id("listing-croissant"), 2);
        var reservation = await ReadAsync<ReservationBody>(response, HttpStatusCode.Created);
        reservation.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe("Reserved"),
            r => r.Quantity.ShouldBe(2),
            r => r.PickupCode.ShouldMatch("^[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$"));

        var listing = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-croissant")}", Ct));
        listing.QuantityAvailable.ShouldBe(6);
    }

    [Fact]
    public async Task Ten_customers_race_for_the_last_portion_and_exactly_one_gets_it()
    {
        // Leave a single unit on the croissant listing.
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE listings SET quantity_available = 1 WHERE id = {Id("listing-croissant")}", Ct));
        var customers = await Task.WhenAll(Enumerable.Range(1, 10).Select(i => NewCustomerAsync($"Racer{i}")));

        var responses = await Task.WhenAll(customers.Select(c => ReserveAsync(c, Id("listing-croissant"))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await ProblemAsync(loser, HttpStatusCode.Conflict, "SOLD_OUT");
        }
        var listing = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-croissant")}", Ct));
        listing.ShouldSatisfyAllConditions(l => l.QuantityAvailable.ShouldBe(0), l => l.Status.ShouldBe("SoldOut"));
    }

    [Fact]
    public async Task One_customer_double_clicking_gets_one_reservation()
    {
        var customer = await NewCustomerAsync("Clicky");
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ReserveAsync(customer, Id("listing-surprise"))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        var listing = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-surprise")}", Ct));
        listing.QuantityAvailable.ShouldBe(4);
    }

    [Fact]
    public async Task The_same_listing_cannot_be_reserved_twice()
    {
        var maya = await MayaAsync(); // already holds the pastry box
        await ProblemAsync(await ReserveAsync(maya, Id("listing-pastry")), HttpStatusCode.Conflict, "LIMIT_REACHED");
    }

    [Fact]
    public async Task A_customer_can_hold_three_active_reservations()
    {
        var maya = await MayaAsync(); // two active already
        (await ReserveAsync(maya, Id("listing-thali"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var problem = await ProblemAsync(await ReserveAsync(maya, Id("listing-momo")), HttpStatusCode.Conflict, "LIMIT_REACHED");
        problem.Detail!.ShouldContain("up to 3");
    }

    [Fact]
    public async Task Below_60_reliability_only_one_active_reservation_is_allowed()
    {
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE users SET reliability_score = 55 WHERE id = {Id("user-maya")}", Ct));
        var maya = await MayaAsync();
        var problem = await ProblemAsync(await ReserveAsync(maya, Id("listing-thali")), HttpStatusCode.Conflict, "LIMIT_REACHED");
        problem.Detail!.ShouldContain("below 60");
    }

    [Fact]
    public async Task More_than_two_units_is_a_validation_error()
    {
        var maya = await MayaAsync();
        var problem = await ProblemAsync(await ReserveAsync(maya, Id("listing-thali"), 3), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!["quantity"].ShouldContain("Choose between 1 and 2.");
    }

    [Fact]
    public async Task Asking_for_more_than_is_left_is_sold_out()
    {
        var maya = await MayaAsync();
        await ProblemAsync(await ReserveAsync(maya, Id("listing-juice")), HttpStatusCode.Conflict, "SOLD_OUT");
    }

    [Fact]
    public async Task An_ended_listing_is_closed()
    {
        var maya = await MayaAsync();
        await ProblemAsync(await ReserveAsync(maya, Id("listing-old-bagels")), HttpStatusCode.Conflict, "WINDOW_CLOSED");
    }

    [Fact]
    public async Task Cancelling_before_the_window_is_free_and_returns_the_stock()
    {
        var maya = await MayaAsync();
        var cancelled = await ReadAsync<ReservationBody>(
            await maya.PostAsync($"/api/v1/reservations/{Id("res-maya-pastry")}/cancel", null, Ct));
        cancelled.ShouldSatisfyAllConditions(r => r.Status.ShouldBe("Cancelled"), r => r.LateCancel.ShouldBeFalse());

        var listing = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-pastry")}", Ct));
        listing.QuantityAvailable.ShouldBe(4);
        (await ReadAsync<UserBody>(await maya.GetAsync("/api/v1/users/me", Ct))).ReliabilityScore.ShouldBe(90);
    }

    [Fact]
    public async Task Cancelling_after_the_window_opened_costs_five_reliability()
    {
        var maya = await MayaAsync();
        var reservation = await ReadAsync<ReservationBody>(await ReserveAsync(maya, Id("listing-thali")), HttpStatusCode.Created);
        var cancelled = await ReadAsync<ReservationBody>(await maya.PostAsync($"/api/v1/reservations/{reservation.Id}/cancel", null, Ct));
        cancelled.LateCancel.ShouldBeTrue();
        (await ReadAsync<UserBody>(await maya.GetAsync("/api/v1/users/me", Ct))).ReliabilityScore.ShouldBe(85);
    }

    [Fact]
    public async Task Two_parallel_cancels_return_the_stock_only_once()
    {
        var maya = await MayaAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            maya.PostAsync($"/api/v1/reservations/{Id("res-maya-pastry")}/cancel", null, Ct)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var listing = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-pastry")}", Ct));
        listing.QuantityAvailable.ShouldBe(4);
    }

    [Fact]
    public async Task Staff_hand_over_food_with_the_code_and_the_customer_gains_reliability()
    {
        var arjun = await ArjunAsync();
        var collected = await ReadAsync<ReservationBody>(
            await arjun.PostAsJsonAsync("/api/v1/reservations/verify", new { pickupCode = "k7m2 9qxa" }, Ct));
        collected.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe("Collected"),
            r => r.CustomerName.ShouldBe("Maya Rai"),
            r => r.ListingTitle.ShouldBe("Mixed pastry box"));

        var maya = await MayaAsync();
        (await ReadAsync<UserBody>(await maya.GetAsync("/api/v1/users/me", Ct))).ReliabilityScore.ShouldBe(92);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var arjun = await ArjunAsync();
        await arjun.PostAsJsonAsync("/api/v1/reservations/verify", new { pickupCode = "K7M2-9QXA" }, Ct);
        var again = await arjun.PostAsJsonAsync("/api/v1/reservations/verify", new { pickupCode = "K7M2-9QXA" }, Ct);
        await ProblemAsync(again, HttpStatusCode.Conflict, "CODE_ALREADY_USED");
    }

    [Fact]
    public async Task A_code_from_another_business_looks_unknown()
    {
        var maya = await MayaAsync();
        var thali = await ReadAsync<ReservationBody>(await ReserveAsync(maya, Id("listing-thali")), HttpStatusCode.Created);
        var arjun = await ArjunAsync(); // Sunrise Bakery, not Green Leaf Cafe
        var response = await arjun.PostAsJsonAsync("/api/v1/reservations/verify", new { pickupCode = thali.PickupCode }, Ct);
        await ProblemAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Other_people_cannot_see_a_reservation()
    {
        var bikash = await SignInAsync("bikash@example.com");
        await ProblemAsync(await bikash.GetAsync($"/api/v1/reservations/{Id("res-maya-pastry")}", Ct), HttpStatusCode.NotFound, "NOT_FOUND");
        var arjun = await ArjunAsync(); // the listing's business can
        (await arjun.GetAsync($"/api/v1/reservations/{Id("res-maya-pastry")}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task My_reservations_split_into_active_and_history()
    {
        var maya = await MayaAsync();
        var active = await ReadAsync<PageBody<ReservationBody>>(await maya.GetAsync("/api/v1/reservations/mine", Ct));
        active.Items.Select(r => r.PickupCode).ShouldBe(["K7M2-9QXA", "R4TW-8HNE"]); // soonest pickup first
        var history = await ReadAsync<PageBody<ReservationBody>>(await maya.GetAsync("/api/v1/reservations/mine?status=history", Ct));
        history.Total.ShouldBe(4);
    }

    [Fact]
    public async Task The_counter_queue_shows_todays_pickups_waiting_first()
    {
        var arjun = await ArjunAsync();
        var queue = await ReadAsync<PageBody<ReservationBody>>(
            await arjun.GetAsync($"/api/v1/organizations/{Id("org-sunrise")}/reservations", Ct));
        queue.Items.Select(r => r.CustomerName).ShouldBe(["Maya Rai", "Bikash Karki", "Binod Khadka", "Sita Tamang"], ignoreOrder: true);
        queue.Items.Last().Status.ShouldBe("Collected");
    }
}

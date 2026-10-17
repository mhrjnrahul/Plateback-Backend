using System.Net;
using System.Net.Http.Json;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

public class JobTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task After_the_window_ends_reservations_become_no_shows_exactly_once()
    {
        // The pastry box window ends 3 hours after the test clock's start.
        Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(1));

        var first = await Factory.RunExpiryAsync();
        var second = await Factory.RunExpiryAsync();

        first.NoShows.ShouldBe(3); // Maya and Bikash (pastry box), Binod (sourdough)
        second.ShouldBe((0, 0));
        var maya = await MayaAsync();
        (await ReadAsync<UserBody>(await maya.GetAsync("/api/v1/users/me", Ct))).ReliabilityScore.ShouldBe(70);
        var pastry = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-pastry")}", Ct));
        pastry.Status.ShouldBe("Expired");
    }

    [Fact]
    public async Task Notifications_are_delivered_by_the_outbox_job()
    {
        var maya = await MayaAsync();
        var before = await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct));
        (await maya.PostAsJsonAsync($"/api/v1/listings/{Id("listing-thali")}/reservations", new { quantity = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Written to the outbox in the reservation's transaction, delivered by the job.
        (await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct))).Total.ShouldBe(before.Total);
        (await Factory.RunOutboxAsync()).ShouldBe(2); // one for Maya, one for Green Leaf Cafe's staff
        var after = await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct));
        after.Total.ShouldBe(before.Total + 1);
        after.UnreadCount.ShouldBe(before.UnreadCount + 1);
        after.Items[0].ShouldSatisfyAllConditions(
            n => n.Type.ShouldBe("ReservationConfirmed"),
            n => n.Body.ShouldContain("Veg thali set"));
    }

    [Fact]
    public async Task Marking_notifications_read_updates_the_unread_count()
    {
        var maya = await MayaAsync();
        (await maya.PostAsync("/api/v1/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync<NotificationPageBody>(await maya.GetAsync("/api/v1/notifications", Ct))).UnreadCount.ShouldBe(0);
    }
}

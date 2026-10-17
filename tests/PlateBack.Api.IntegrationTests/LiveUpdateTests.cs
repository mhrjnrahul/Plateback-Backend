using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

/// <summary>SignalR pushes from /hubs/live, received by a real client over the in-memory server.</summary>
public class LiveUpdateTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private async Task<HubConnection> ConnectAsync(string? accessToken = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/live"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
        await connection.StartAsync(Ct);
        return connection;
    }

    private static TaskCompletionSource<JsonElement> Listen(HubConnection connection, string method)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(method, message => received.TrySetResult(message));
        return received;
    }

    private async Task<string> TokenAsync(string email) => (await LoginAsync(Anonymous(), email)).AccessToken;

    [Fact]
    public async Task Visitors_hear_when_stock_changes()
    {
        await using var visitor = await ConnectAsync();
        var changed = Listen(visitor, "ListingsChanged");

        var maya = await MayaAsync();
        (await maya.PostAsJsonAsync($"/api/v1/listings/{Id("listing-croissant")}/reservations", new { quantity = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var message = await changed.Task.WaitAsync(Wait, Ct);
        message.GetProperty("listingIds").EnumerateArray().Select(e => e.GetGuid()).ShouldContain(Id("listing-croissant"));
    }

    [Fact]
    public async Task Only_the_business_staff_hear_about_its_queue()
    {
        await using var staff = await ConnectAsync(await TokenAsync("arjun@sunrisebakery.example"));
        await staff.InvokeAsync("WatchOrganization", Id("org-sunrise"), Ct);
        var staffHeard = Listen(staff, "OrganizationChanged");
        await using var visitor = await ConnectAsync();
        var visitorHeard = Listen(visitor, "OrganizationChanged");

        var maya = await MayaAsync();
        (await maya.PostAsJsonAsync($"/api/v1/listings/{Id("listing-croissant")}/reservations", new { quantity = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await staffHeard.Task.WaitAsync(Wait, Ct)).GetProperty("organizationId").GetGuid().ShouldBe(Id("org-sunrise"));
        await Task.Delay(300, Ct);
        visitorHeard.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Customers_cannot_watch_a_business_they_do_not_belong_to()
    {
        await using var customer = await ConnectAsync(await TokenAsync("maya@example.com"));
        var error = await Should.ThrowAsync<HubException>(() => customer.InvokeAsync("WatchOrganization", Id("org-sunrise"), Ct));
        error.Message.ShouldContain("not a member");

        await using var visitor = await ConnectAsync();
        await Should.ThrowAsync<HubException>(() => visitor.InvokeAsync("WatchOrganization", Id("org-sunrise"), Ct));
    }

    [Fact]
    public async Task Customers_hear_about_new_notifications_once_the_outbox_delivers_them()
    {
        await using var customer = await ConnectAsync(await TokenAsync("maya@example.com"));
        var heard = Listen(customer, "NotificationsChanged");

        var maya = await MayaAsync();
        (await maya.PostAsJsonAsync($"/api/v1/listings/{Id("listing-thali")}/reservations", new { quantity = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        heard.Task.IsCompleted.ShouldBeFalse();

        await Factory.RunOutboxAsync();
        await heard.Task.WaitAsync(Wait, Ct);
    }
}

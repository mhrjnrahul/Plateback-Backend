using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using PlateBack.Infrastructure.Persistence.Seed;

namespace PlateBack.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Base for integration tests: fresh demo data before each test, plus helpers
/// for signing in and reading responses the way the frontend does.
/// </summary>
public abstract class ApiTestBase(PlateBackFactory factory) : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected PlateBackFactory Factory { get; } = factory;

    protected TestClock Clock => Factory.Clock;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public virtual async ValueTask InitializeAsync() => await Factory.ResetAsync();

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static Guid Id(string seedKey) => SeedIds.For(seedKey);

    protected HttpClient Anonymous() => Factory.CreateClient();

    /// <summary>A client signed in as a demo account (all use password123).</summary>
    protected async Task<HttpClient> SignInAsync(string email)
    {
        var client = Factory.CreateClient();
        var auth = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    protected static async Task<AuthBody> LoginAsync(HttpClient client, string email, string password = DemoDataSeeder.Password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AuthBody>(Json, Ct))!;
    }

    protected Task<HttpClient> MayaAsync() => SignInAsync("maya@example.com");
    protected Task<HttpClient> ArjunAsync() => SignInAsync("arjun@sunrisebakery.example");
    protected Task<HttpClient> AdminAsync() => SignInAsync("admin@plateback.example");

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    /// <summary>Asserts an error response: status, ProblemDetails content type and code.</summary>
    protected static async Task<ProblemBody> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = JsonSerializer.Deserialize<ProblemBody>(body, Json)!;
        problem.Code.ShouldBe(code, body);
        problem.Status.ShouldBe((int)status);
        return problem;
    }
}

public sealed record ProblemBody(string Type, string Title, int Status, string? Detail, string Code, Dictionary<string, string[]>? Errors);

public sealed record UserBody(Guid Id, string Email, string DisplayName, string Role, int ReliabilityScore, bool IsSuspended, List<MembershipBody> Memberships);

public sealed record MembershipBody(Guid OrganizationId, string OrganizationName, string Role);

public sealed record AuthBody(string AccessToken, int ExpiresIn, UserBody User);

public sealed record PageBody<T>(List<T> Items, int Page, int PageSize, int Total);

public sealed record OrgSummaryBody(Guid Id, string Name, bool IsVerified);

public sealed record ListingBody(
    Guid Id, string Title, string Category, OrgSummaryBody Organization, int Price, int OriginalPrice, int QuantityTotal,
    int QuantityAvailable, DateTimeOffset PickupStart, DateTimeOffset PickupEnd, string Status, double? DistanceKm,
    string? ImageUrl, int? MaxPerUser);

public sealed record ReservationBody(
    Guid Id, Guid ListingId, int Quantity, string Status, string PickupCode, DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? CollectedAt, DateTimeOffset? CancelledAt, bool LateCancel,
    string? CustomerName, string? ListingTitle);

public sealed record NotificationBody(Guid Id, string Type, string Title, string Body, bool IsRead, Dictionary<string, string> Data);

public sealed record NotificationPageBody(List<NotificationBody> Items, int Total, int UnreadCount);

public sealed record OrganizationBody(Guid Id, string Name, string Type, string Status, string? RejectionReason, string? OwnerName, string? OwnerEmail);

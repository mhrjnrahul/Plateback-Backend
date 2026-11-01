using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PlateBack.Api.IntegrationTests.Infrastructure;
using PlateBack.Infrastructure.Persistence;
using PlateBack.Infrastructure.Persistence.Seed;

namespace PlateBack.Api.IntegrationTests;

/// <summary>Week 6: how the API behaves when deployed (start-up database work, hosting the React app, photo storage).</summary>
public class DeploymentTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private const string EmptyEverything = """
        TRUNCATE notifications, audit_logs, outbox_messages, reservations, listings, organization_members,
                 organizations, refresh_tokens, users, identity_user_tokens, identity_user_logins,
                 identity_user_claims, identity_users CASCADE
        """;

    private static Task<HttpResponseMessage> TryLoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, Ct);

    // ---------- Start-up database work ----------

    [Fact]
    public async Task Seeding_if_empty_never_touches_a_database_that_has_accounts()
    {
        var maya = await MayaAsync();
        (await maya.PostAsJsonAsync($"/api/v1/listings/{Id("listing-thali")}/reservations", new { quantity = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedIfEmptyAsync(Ct)).ShouldBeFalse();

        var mine = await ReadAsync<PageBody<ReservationBody>>(await maya.GetAsync("/api/v1/reservations/mine", Ct));
        mine.Items.ShouldContain(r => r.ListingId == Id("listing-thali"));
    }

    [Fact]
    public async Task A_new_database_gets_the_demo_data_on_first_start_with_a_private_admin_password()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(EmptyEverything, Ct);
        }

        await using var deployed = Factory.WithWebHostBuilder(b => b
            .UseSetting("Database:MigrateOnStartup", "true")
            .UseSetting("Database:SeedDemoIfEmpty", "true")
            .UseSetting("Demo:AdminPassword", "a-long-private-admin-password"));
        var client = deployed.CreateClient();

        (await TryLoginAsync(client, "maya@example.com", "password123")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TryLoginAsync(client, "admin@plateback.example", "password123")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TryLoginAsync(client, "admin@plateback.example", "a-long-private-admin-password")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deployed_site_refuses_to_seed_an_admin_with_the_published_password()
    {
        await using var deployed = Factory.WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("Database:SeedDemoIfEmpty", "true")
            .UseSetting("Demo:AdminPassword", ""));

        var error = Should.Throw<Exception>(() => deployed.CreateClient());
        error.ToString().ShouldContain("Demo:AdminPassword");
    }

    // ---------- Keeping the public demo fresh ----------

    [Fact]
    public async Task A_demo_whose_listings_have_all_ended_is_refreshed_with_new_pickup_windows()
    {
        // Two days later every seeded pickup window has ended: the live site would look empty.
        Clock.Advance(TimeSpan.FromDays(2));
        var refresher = Factory.Services.GetRequiredService<DemoRefreshService>();

        (await refresher.RefreshIfStaleAsync(Ct)).ShouldBeTrue();

        var croissant = await ReadAsync<ListingBody>(await Anonymous().GetAsync($"/api/v1/listings/{Id("listing-croissant")}", Ct));
        croissant.PickupEnd.ShouldBeGreaterThan(Clock.GetUtcNow());
        croissant.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task A_demo_with_live_listings_is_left_alone()
    {
        var maya = await MayaAsync();
        (await maya.PostAsJsonAsync($"/api/v1/listings/{Id("listing-thali")}/reservations", new { quantity = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await Factory.Services.GetRequiredService<DemoRefreshService>().RefreshIfStaleAsync(Ct)).ShouldBeFalse();

        var mine = await ReadAsync<PageBody<ReservationBody>>(await maya.GetAsync("/api/v1/reservations/mine", Ct));
        mine.Items.ShouldContain(r => r.ListingId == Id("listing-thali"));
    }

    // ---------- Hosting the React app ----------

    private sealed class FrontendBuild : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"plateback-wwwroot-{Guid.NewGuid():N}");

        public FrontendBuild()
        {
            Directory.CreateDirectory(Path.Combine(Folder, "assets"));
            File.WriteAllText(Path.Combine(Folder, "index.html"), "<!doctype html><title>PlateBack app shell</title>");
            File.WriteAllText(Path.Combine(Folder, "assets", "index-abc123.js"), "console.log('app');");
        }

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    private WebApplicationFactory<Program> WithFrontend(FrontendBuild build) =>
        Factory.WithWebHostBuilder(b => b.UseSetting(WebHostDefaults.WebRootKey, build.Folder));

    [Theory]
    [InlineData("/")]
    [InlineData("/browse")]
    [InlineData("/me/reservations")]
    [InlineData("/listings/c7d99bc5-ef83-3309-9f92-8dfb436d79a1")]
    public async Task App_pages_get_the_React_app_with_a_strict_security_policy(string path)
    {
        using var build = new FrontendBuild();
        await using var app = WithFrontend(build);
        var response = await app.CreateClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("PlateBack app shell");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue(); // a new release is picked up at once
        var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        policy.ShouldContain("script-src 'self'");
        policy.ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Built_assets_are_cached_for_a_year()
    {
        using var build = new FrontendBuild();
        await using var app = WithFrontend(build);
        var response = await app.CreateClient().GetAsync("/assets/index-abc123.js", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromDays(365));
    }

    [Fact]
    public async Task Unknown_API_paths_stay_JSON_errors_next_to_the_app()
    {
        using var build = new FrontendBuild();
        await using var app = WithFrontend(build);
        var client = app.CreateClient();

        await ProblemAsync(await client.GetAsync("/api/v1/nothing-here", Ct), HttpStatusCode.NotFound, "NOT_FOUND");
        var health = await client.GetAsync("/health", Ct);
        health.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    // ---------- Photos in Blob Storage ----------

    [Theory]
    [InlineData("evil.svg")]
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("0193a1b2c3d4e5f60718293a4b5c6d7e.exe")]
    public async Task With_Blob_Storage_a_name_the_API_never_issued_is_not_looked_up(string name)
    {
        // No storage account exists at this address: the name is rejected before any call.
        await using var app = Factory.WithWebHostBuilder(b => b
            .UseSetting("Storage:Provider", "Blob")
            .UseSetting("Storage:BlobServiceUri", "https://plateback-test.invalid"));
        var response = await app.CreateClient().GetAsync($"/uploads/{name}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

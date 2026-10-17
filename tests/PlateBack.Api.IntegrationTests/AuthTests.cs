using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

public class AuthTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private static string RefreshCookieFrom(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("plateback_refresh=", StringComparison.Ordinal));
        return header.Split(';')[0];
    }

    private async Task<HttpResponseMessage> RefreshAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return await Anonymous().SendAsync(request, Ct);
    }

    [Fact]
    public async Task Register_signs_the_new_customer_in_with_a_safe_refresh_cookie()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new { displayName = "Asha Karki", email = "asha@example.com", password = "longenough" }, Ct);

        var body = await ReadAsync<AuthBody>(response, HttpStatusCode.Created);
        body.ExpiresIn.ShouldBe(900);
        body.User.ShouldSatisfyAllConditions(
            u => u.Email.ShouldBe("asha@example.com"),
            u => u.Role.ShouldBe("Customer"),
            u => u.ReliabilityScore.ShouldBe(100));
        var cookie = response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/v1/auth");
    }

    [Fact]
    public async Task Register_reports_every_invalid_field()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new { displayName = "A", email = "nope", password = "short" }, Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!.Keys.ShouldBe(["displayName", "email", "password"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_email_can_only_be_registered_once()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new { displayName = "Maya Again", email = "MAYA@example.com", password = "longenough" }, Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!["email"].ShouldContain("An account with this email already exists.");
    }

    [Fact]
    public async Task A_wrong_password_is_401()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "maya@example.com", password = "wrong-password" }, Ct);
        await ProblemAsync(response, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var client = Anonymous();
        for (var i = 0; i < 4; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "maya@example.com", password = "wrong" }, Ct);
            attempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        var fifth = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "maya@example.com", password = "wrong" }, Ct);
        await ProblemAsync(fifth, HttpStatusCode.TooManyRequests, "RATE_LIMITED");

        var correct = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "maya@example.com", password = "password123" }, Ct);
        await ProblemAsync(correct, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    [Fact]
    public async Task Protected_endpoints_need_a_token()
    {
        await ProblemAsync(await Anonymous().GetAsync("/api/v1/users/me", Ct), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task An_expired_access_token_says_so()
    {
        var maya = await MayaAsync();
        Clock.Advance(TimeSpan.FromMinutes(16));
        await ProblemAsync(await maya.GetAsync("/api/v1/users/me", Ct), HttpStatusCode.Unauthorized, "TOKEN_EXPIRED");
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reusing_an_old_one_ends_every_session()
    {
        var login = await Anonymous().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "maya@example.com", password = "password123" }, Ct);
        var first = RefreshCookieFrom(login);

        var refreshed = await RefreshAsync(first);
        (await ReadAsync<AuthBody>(refreshed)).User.Email.ShouldBe("maya@example.com");
        var second = RefreshCookieFrom(refreshed);
        second.ShouldNotBe(first);

        // Someone replays the first (already rotated) token: theft is assumed.
        await ProblemAsync(await RefreshAsync(first), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        // ...so the legitimate newer token stops working too.
        await ProblemAsync(await RefreshAsync(second), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var login = await Anonymous().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "maya@example.com", password = "password123" }, Ct);
        var cookie = RefreshCookieFrom(login);
        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", cookie);
        (await Anonymous().SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ProblemAsync(await RefreshAsync(cookie), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task The_profile_shows_business_memberships()
    {
        var arjun = await ArjunAsync();
        var me = await ReadAsync<UserBody>(await arjun.GetAsync("/api/v1/users/me", Ct));
        me.Memberships.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            m => m.OrganizationName.ShouldBe("Sunrise Bakery"),
            m => m.Role.ShouldBe("Owner"));
    }

    [Fact]
    public async Task Users_can_change_their_display_name()
    {
        var maya = await MayaAsync();
        var me = await ReadAsync<UserBody>(await maya.PatchAsJsonAsync("/api/v1/users/me", new { displayName = "Maya R." }, Ct));
        me.DisplayName.ShouldBe("Maya R.");
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var client = Anonymous();
        var token = (await LoginAsync(client, "maya@example.com")).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");
        await ProblemAsync(await client.GetAsync("/api/v1/users/me", Ct), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }
}

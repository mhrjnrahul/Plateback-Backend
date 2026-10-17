using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using Microsoft.AspNetCore.Hosting;

using PlateBack.Api.IntegrationTests.Infrastructure;

namespace PlateBack.Api.IntegrationTests;

public class UploadTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private sealed record UploadBody(string Url);

    // The smallest valid PNG: 1x1 transparent pixel.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task A_photo_is_stored_and_served()
    {
        var arjun = await ArjunAsync();
        var upload = await ReadAsync<UploadBody>(
            await arjun.PostAsync("/api/v1/uploads/images", Form(Png, "bread.png", "image/png"), Ct), HttpStatusCode.Created);
        upload.Url.ShouldStartWith("/uploads/");
        upload.Url.ShouldEndWith(".png");

        var served = await Anonymous().GetAsync(upload.Url, Ct);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        (await served.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
    }

    [Fact]
    public async Task A_text_file_renamed_to_jpg_is_rejected()
    {
        var arjun = await ArjunAsync();
        var response = await arjun.PostAsync("/api/v1/uploads/images",
            Form(Encoding.UTF8.GetBytes("definitely not a photo"), "photo.jpg", "image/jpeg"), Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        problem.Errors!["file"].ShouldContain("Use a JPEG, PNG or WebP photo.");
    }

    [Fact]
    public async Task Uploading_needs_an_account()
    {
        await ProblemAsync(await Anonymous().PostAsync("/api/v1/uploads/images", Form(Png, "a.png", "image/png"), Ct),
            HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }
}

public class ErrorFormatTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Unknown_routes_answer_with_problem_details()
    {
        await ProblemAsync(await Anonymous().GetAsync("/api/v1/nothing-here", Ct), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Malformed_json_is_a_400_not_a_500()
    {
        var maya = await MayaAsync();
        var content = new StringContent("{ \"quantity\": ", Encoding.UTF8, "application/json");
        await ProblemAsync(await maya.PostAsync($"/api/v1/listings/{Id("listing-thali")}/reservations", content, Ct),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task A_non_guid_id_does_not_match_any_route()
    {
        await ProblemAsync(await Anonymous().GetAsync("/api/v1/listings/not-a-guid", Ct), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/listings");
        request.Headers.Add("X-Correlation-Id", "test-123");
        var response = await Anonymous().SendAsync(request, Ct);
        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["test-123"]);
    }

    [Fact]
    public async Task Health_reports_the_database()
    {
        var health = await ReadAsync<Dictionary<string, string>>(await Anonymous().GetAsync("/health", Ct));
        health["database"].ShouldBe("Connected");
    }

    [Fact]
    public async Task The_OpenAPI_document_describes_every_endpoint()
    {
        var document = await Anonymous().GetStringAsync("/openapi/v1.json", Ct);
        foreach (var path in new[] { "/api/v1/auth/login", "/api/v1/listings", "/api/v1/listings/{id}/reservations", "/api/v1/reservations/verify", "/api/v1/admin/organizations", "/api/v1/uploads/images" })
        {
            document.ShouldContain($"\"{path}\"");
        }
        document.ShouldContain("\"Bearer\"");
    }
}

public class RateLimitTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Too_many_login_attempts_from_one_address_get_429()
    {
        await using var strict = Factory.WithWebHostBuilder(b => b.UseSetting("RateLimits:AuthPerMinute", "3"));
        var client = strict.CreateClient();
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            responses.Add(await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@example.com", password = "whatever1" }, Ct));
        }
        responses.Take(3).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var limited = await ProblemAsync(responses[3], HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        limited.Detail.ShouldNotBeNull();
        responses[3].Headers.RetryAfter.ShouldNotBeNull();
    }
}

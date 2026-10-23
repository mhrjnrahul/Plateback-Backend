using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using PlateBack.Api.IntegrationTests.Infrastructure;
using PlateBack.Application.Common.Abstractions;

using Serilog.Core;
using Serilog.Events;

namespace PlateBack.Api.IntegrationTests;

/// <summary>What the API writes to its log when a request fails (tests log Warning and above).</summary>
public class LoggingTests(PlateBackFactory factory) : ApiTestBase(factory)
{
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    /// <summary>A storage failure nobody planned for, like a full disk.</summary>
    private sealed class FullDisk : IImageStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken) =>
            throw new IOException("No space left on device: /srv/plateback/uploads");
    }

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private WebApplicationFactory<Program> WithLog(CapturingSink sink, bool fullDisk = false) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(sink);
            if (fullDisk)
            {
                services.RemoveAll<IImageStorage>();
                services.AddSingleton<IImageStorage, FullDisk>();
            }
        }));

    [Fact]
    public async Task An_unexpected_failure_is_logged_once_with_the_trace_id_the_client_sees()
    {
        var sink = new CapturingSink();
        await using var app = WithLog(sink, fullDisk: true);
        var client = app.CreateClient();
        var auth = await LoginAsync(client, "arjun@sunrisebakery.example");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await client.PostAsync("/api/v1/uploads/images",
            new MultipartFormDataContent { { file, "file", "bread.png" } }, Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain("No space"); // no internal details for the client
        var traceId = JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString();

        // Before Week 4 this one failure wrote three errors with the same stack
        // trace (exception middleware, ApiExceptionHandler, request log).
        var error = sink.Events.Where(e => e.Level >= LogEventLevel.Error).ShouldHaveSingleItem();
        error.Exception.ShouldBeOfType<IOException>();
        error.Properties["RequestPath"].ToString().ShouldBe("\"/api/v1/uploads/images\"");
        error.Properties["StatusCode"].ToString().ShouldBe("500");
        error.Properties["CorrelationId"].ToString().Trim('"').ShouldBe(traceId);
    }

    [Fact]
    public async Task Expected_failures_are_not_logged_as_errors()
    {
        var sink = new CapturingSink();
        await using var app = WithLog(sink);
        var client = app.CreateClient();
        var auth = await LoginAsync(client, "arjun@sunrisebakery.example");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // A wrong pickup code, an unknown listing and a malformed body: normal answers.
        (await client.PostAsync("/api/v1/reservations/verify",
            new StringContent("""{ "pickupCode": "ZZZZ-ZZZZ" }""", System.Text.Encoding.UTF8, "application/json"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/listings/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync("/api/v1/reservations/verify",
            new StringContent("{ \"pickupCode\": ", System.Text.Encoding.UTF8, "application/json"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        sink.Events.Where(e => e.Level >= LogEventLevel.Error).ShouldBeEmpty();
    }
}

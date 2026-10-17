using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PlateBack.Application.Features.Jobs;
using PlateBack.Infrastructure.Persistence;
using PlateBack.Infrastructure.Persistence.Seed;

namespace PlateBack.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against a real PostgreSQL database
/// (plateback_test by default, or PLATEBACK_TEST_DB). Real PostgreSQL rather
/// than an in-memory provider, because the CHECK constraints, unique indexes,
/// row locks and transactions are exactly what the tests need to prove.
/// </summary>
public sealed class PlateBackFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=plateback_test;Username=plateback;Password=plateback";

    public TestClock Clock { get; } = new();

    public string UploadFolder { get; } = Path.Combine(Path.GetTempPath(), "plateback-test-uploads");

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("PLATEBACK_TEST_DB") ?? DefaultConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Jobs:Enabled", "false"); // tests run the jobs themselves
        builder.UseSetting("RateLimits:AuthPerMinute", "10000");
        builder.UseSetting("RateLimits:PickupCodesPerMinute", "10000");
        builder.UseSetting("Uploads:Path", UploadFolder);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async ValueTask InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    /// <summary>Puts the clock back and reloads the demo data. Called before every test.</summary>
    public async Task ResetAsync()
    {
        Clock.Now = TestClock.Start;
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAsync();
    }

    /// <summary>Runs code against the database directly, for setting up or checking state.</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<int> RunOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().RunOnceAsync(CancellationToken.None);
    }

    public async Task<(int Expired, int NoShows)> RunExpiryAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExpireListingsJob>().RunOnceAsync(CancellationToken.None);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PlateBack.Domain.Enums;

namespace PlateBack.Infrastructure.Persistence.Seed;

/// <summary>
/// Keeps a public demo deployment usable (Demo:RefreshWhenStale, Week 6).
/// The demo listings' pickup windows are set relative to when they were
/// seeded, so after about a day every one has ended and the site looks empty
/// to a reviewer. At start-up and every 30 minutes this checks whether any
/// demo listing is still live; once none is, the demo data is reset with fresh
/// windows. Changes people made on the demo are cleared at that point.
/// </summary>
public sealed partial class DemoRefreshService(IServiceScopeFactory scopes, TimeProvider time, ILogger<DemoRefreshService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <summary>The seeded listings that are meant to be live (the "old-" ones have ended by design).</summary>
    private static readonly Guid[] LiveDemoListings =
    [
        .. new[]
        {
            "listing-pastry", "listing-croissant", "listing-sourdough", "listing-thali", "listing-bread",
            "listing-salad", "listing-momo", "listing-surprise", "listing-chiya", "listing-juice", "listing-vegbox",
        }.Select(SeedIds.For),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await RefreshIfStaleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRefreshFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>Whether the demo data was reset.</returns>
    public async Task<bool> RefreshIfStaleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow();
        var stillLive = await db.Listings.AnyAsync(
            l => LiveDemoListings.Contains(l.Id)
                && (l.Status == ListingStatus.Active || l.Status == ListingStatus.SoldOut)
                && l.PickupEnd > now,
            cancellationToken);
        if (stillLive)
        {
            return false;
        }
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAsync(cancellationToken);
        LogRefreshed(logger);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Every demo listing had ended; the demo data was reset with fresh pickup windows")]
    private static partial void LogRefreshed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refreshing the demo data failed")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}

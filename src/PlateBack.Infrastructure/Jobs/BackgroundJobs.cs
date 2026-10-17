using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PlateBack.Application.Features.Jobs;

namespace PlateBack.Infrastructure.Jobs;

/// <summary>"Jobs" section: switch the scheduled jobs off (tests) or change how often they run.</summary>
public sealed class JobOptions
{
    public const string Section = "Jobs";

    public bool Enabled { get; set; } = true;
    public int ExpiryIntervalSeconds { get; set; } = 60;
    public int OutboxIntervalSeconds { get; set; } = 10;
}

/// <summary>
/// Runs the two scheduled jobs from design doc 2.6 inside the API process
/// (decision 5: hosted services instead of Hangfire). Each run gets its own
/// scope and database context; a failure is logged and retried next time.
/// </summary>
internal sealed partial class BackgroundJobs(
    IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<BackgroundJobs> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return Task.CompletedTask;
        }
        return Task.WhenAll(
            RunEveryAsync<ExpireListingsJob>(options.Value.ExpiryIntervalSeconds, j => j.RunOnceAsync(stoppingToken), stoppingToken),
            RunEveryAsync<ProcessOutboxJob>(options.Value.OutboxIntervalSeconds, j => j.RunOnceAsync(stoppingToken), stoppingToken));
    }

    private async Task RunEveryAsync<TJob>(int seconds, Func<TJob, Task> run, CancellationToken stoppingToken)
        where TJob : notnull
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await run(scope.ServiceProvider.GetRequiredService<TJob>());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A job failure must not stop the scheduler; it is logged and retried.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogJobFailed(logger, ex, typeof(TJob).Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background job {Job} failed")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string job);
}

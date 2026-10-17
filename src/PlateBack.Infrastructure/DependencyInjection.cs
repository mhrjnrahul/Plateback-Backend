using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PlateBack.Application.Common.Abstractions;
using PlateBack.Infrastructure.Identity;
using PlateBack.Infrastructure.Jobs;
using PlateBack.Infrastructure.Persistence;
using PlateBack.Infrastructure.Persistence.Seed;
using PlateBack.Infrastructure.Storage;

namespace PlateBack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not set. Use `dotnet user-secrets set \"ConnectionStrings:Default\" \"...\"` (see README).");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Matches the frontend form: at least 8 characters, no other rules.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<DemoDataSeeder>();

        services.AddOptions<UploadOptions>().Bind(configuration.GetSection(UploadOptions.Section));
        services.AddSingleton<IImageStorage, LocalImageStorage>();

        services.AddOptions<JobOptions>().Bind(configuration.GetSection(JobOptions.Section));
        services.AddHostedService<BackgroundJobs>();
        return services;
    }
}

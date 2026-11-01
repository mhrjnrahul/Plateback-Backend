namespace PlateBack.Infrastructure.Persistence.Seed;

/// <summary>Demo data settings ("Demo" section).</summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>
    /// Password for the demo admin account. Unset (development, tests): the same
    /// password123 as every demo account. Required where the site is public.
    /// </summary>
    public string? AdminPassword { get; set; }

    /// <summary>
    /// For a public demo: reset the demo data once every demo listing has
    /// ended, so the site never looks empty (see DemoRefreshService).
    /// </summary>
    public bool RefreshWhenStale { get; set; }
}

/// <summary>What the API does to the database when it starts ("Database" section).</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Apply pending EF Core migrations before serving requests.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Insert the demo data if the database has no accounts yet. Never deletes anything.</summary>
    public bool SeedDemoIfEmpty { get; set; }
}

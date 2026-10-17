namespace PlateBack.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A clock the tests control. Every time-based rule (pickup windows, token
/// expiry, the expiry job) reads it through TimeProvider.
/// </summary>
public sealed class TestClock : TimeProvider
{
    /// <summary>A fixed morning in Kathmandu (11:45 local), so "today" never crosses midnight mid-test.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; } = Start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

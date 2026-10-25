using PlateBack.Domain.Entities;

namespace PlateBack.Domain.Tests;

/// <summary>Week 5: the retry rules were at 38% coverage; a bug here loses or repeats notifications.</summary>
public class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_failed_message_stays_pending_and_counts_the_attempt()
    {
        var message = OutboxMessage.Create("Notification", "{}", Now);
        message.MarkFailed("database timeout", Now);

        message.Attempts.ShouldBe(1);
        message.LastError.ShouldBe("database timeout");
        message.ProcessedAt.ShouldBeNull(); // the job picks it up again
    }

    [Fact]
    public void After_the_last_attempt_the_message_is_given_up_and_keeps_its_error()
    {
        var message = OutboxMessage.Create("Notification", "{}", Now);
        for (var i = 1; i < OutboxMessage.MaxAttempts; i++)
        {
            message.MarkFailed($"attempt {i}", Now);
            message.ProcessedAt.ShouldBeNull();
        }
        message.MarkFailed("attempt 5", Now.AddMinutes(1));

        message.Attempts.ShouldBe(OutboxMessage.MaxAttempts);
        message.ProcessedAt.ShouldBe(Now.AddMinutes(1));
        message.LastError.ShouldBe("attempt 5");
    }

    [Fact]
    public void A_very_long_error_is_cut_to_fit_its_column()
    {
        var message = OutboxMessage.Create("Notification", "{}", Now);
        message.MarkFailed(new string('x', 5000), Now);
        message.LastError!.Length.ShouldBe(1000);
    }
}
